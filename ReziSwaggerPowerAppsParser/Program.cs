using Dezrez.Core.DataContracts.Internal.ExternalProviders;
using Dezrez.Core.DataContracts.Internal.ExternalProviders.EntityChangeSubscribers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace ReziSwaggerPowerAppsParser
{
    class Program
    {

        static void Main(string[] args)
        {
            //Add this to the path foreach
            var newPaths = GetNewPaths();

            var newDescriptions = GetNewDescriptions();

            //Add the definition of the contract

            bool forBusinessUse = true;//Signifies if we'll be able to impersonate all agencies etc.

            bool useImplicitFlow = true;//WHen forBusinessUse = true, specifies to use implicitFlow anyway, instead of using client flow (as client flow doesnt work yet in PowerApps)

            string jsonFileName = args[0];

            string modifiedSwaggerFile = MangleSwagger(forBusinessUse, useImplicitFlow, jsonFileName, newPaths, newDescriptions);

            FileInfo finfo = new FileInfo(modifiedSwaggerFile);

            Console.WriteLine($"{modifiedSwaggerFile} {finfo.Length} bytes in size");

            Console.ReadKey();
        }

        static void Log(string logEntry)
        {
            System.IO.File.AppendAllText("c:\\PowerAppsManglerLogs.txt", $"{logEntry}\r\n");
        }

        private static Dictionary<string, dynamic> GetNewDescriptions()
        {
            var listOfTypes = (from assemblyType in typeof(BaseEntityChangeSubscriptionNotificationDataContract).Assembly.GetTypes()
                               where typeof(BaseEntityChangeSubscriptionNotificationDataContract).IsAssignableFrom(assemblyType)
                               select assemblyType).ToArray();

            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            foreach (var type in listOfTypes)
            {
                string typeName = type.Name;
                var typeDescription = GetTypeDescription(type);
                result.Add(typeName, typeDescription);
            }

            return result;
        }

        private static Dictionary<string, dynamic> GetNewPaths()
        {
            var listOfTypes = (from assemblyType in typeof(BaseEntityChangeSubscriptionNotificationDataContract).Assembly.GetTypes()
                               where typeof(BaseEntityChangeSubscriptionNotificationDataContract).IsAssignableFrom(assemblyType)
                               select assemblyType).ToArray();

            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            foreach (var type in listOfTypes)
            {
                string path = $"/api/webhook/create/{type.Name}";
                var triggerDescrtiption = GetPathDescriptionForTrigger(type);
                result.Add(path, triggerDescrtiption);
            }

            return result;
        }


        private static dynamic GetPathDescriptionForTrigger(Type type)
        {
            string triggerDescriptionText = "";

            var workflowDescriptionAttribute = typeof(WorkflowTriggerDescriptionAttribute);

            var attributes = type.GetCustomAttributes(workflowDescriptionAttribute, true);

            foreach (var attribute in attributes)
            {
                var description = attribute as WorkflowTriggerDescriptionAttribute;
                if (description != null)
                {
                    triggerDescriptionText = description.Description;
                }
            }
            //Trigger description
            string triggerJson = $@"
{{
    ""x-ms-notification-content"": {{
    ""description"": ""Webhook notification details"",
    ""schema"": {{
        ""$ref"": ""#/definitions/{type.Name}""
    }}
    }},
    ""post"": {{
    ""description"": ""Creates a Rezi Webhook"",
    ""summary"": ""{triggerDescriptionText}"",
    ""operationId"": ""webhook-trigger-{type.Name}"",
    ""x-ms-trigger"": ""single"",
    ""parameters"": [     
        {{
        ""name"": ""Request body of webhook"",
        ""in"": ""body"",
        ""description"": ""This is the request body of the Webhook"",
        ""schema"": {{
            ""$ref"": ""#/definitions/Dezrez.Core.DataContracts.External.Api.Webhook.CreateWebhookDataContract""
        }}
        }}
    ],
    ""responses"": {{
                        ""201"": {{
                            ""description"": ""Created"",
                            ""schema"": {{
                                            ""type"":""object""
                                        }}
                                   }}
                   }},
    ""security"": [{{
                    ""dezrezOauth2Implicit"": [""impersonate_user""]
                }}]
}}
                    }}";

            dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(triggerJson);

            return result;
        }

        private static dynamic GetTypeDescription(Type type)
        {
            dynamic result = new JObject();

            result.type = "object";
            result.properties = new JObject();
            foreach (var property in type.GetProperties())
            {
                result.properties[property.Name] = GetPropertyDescription(property.PropertyType);

            }

            return result;
        }

        private static dynamic GetPropertyDescription(Type property)
        {
            string typeName = GetTypeNameFromProperty(property);

            dynamic propertyDescription = new JObject();
            propertyDescription.type = typeName;
            if (property == typeof(long))
            {
                propertyDescription.format = "int64";
            }

            if (property == typeof(DateTime))
            {
                propertyDescription.format = "date-time";
            }

            if (propertyDescription.type == "array")
            {
                if (property.IsGenericList())
                {
                    propertyDescription.items = GetPropertyDescription(property.GetGenericArguments().Single());
                }
                else
                {
                    propertyDescription.items = GetPropertyDescription(property.GetElementType());
                }
            }

            if (propertyDescription.type == "object")
            {
                propertyDescription = GetTypeDescription(property);
            }

            return propertyDescription;
        }

        private static dynamic GetTypeNameFromProperty(Type propertyType)
        {
            string typeName = propertyType.Name;
            if (propertyType == typeof(string))
            {
                return "string";
            }

            if (propertyType == typeof(long) || propertyType == typeof(int))
            {
                return "integer";
            }

            if (propertyType == typeof(bool))
            {
                return "boolean";
            }

            if (propertyType == typeof(DateTime))
            {
                return "string";
            }

            if (propertyType.IsArray || propertyType.IsGenericList())
            {
                return "array";

                /*
                 "type": "array",
					"items": {
						"format": "int64",
						"type": "integer"
					}
                 */
            }

            if (Nullable.GetUnderlyingType(propertyType) != null)
            {
                return GetTypeNameFromProperty(Nullable.GetUnderlyingType(propertyType));
            }

            return "object";
        }



        private static string MangleSwagger(bool forBusinessUse, bool useImplicitFlow, string jsonFileName, Dictionary<string, dynamic> AddPaths = null, Dictionary<string, dynamic> AddDefinitions = null)
        {
            string environmentName = GetEnvironmentNameFromSwaggerURL(jsonFileName);
            Uri fileParamUri = new Uri(jsonFileName);

            string originalSwaggerFile = Path.Combine(Path.GetTempPath(), $"{fileParamUri.Host}.Swagger.Original.json");

            string modifiedSwaggerFile = Path.Combine(Path.GetTempPath(), $"{fileParamUri.Host}.Swagger.Modified.json");

            Console.WriteLine($"Downloading {jsonFileName}");

            if (!File.Exists(modifiedSwaggerFile))
            {
                WebClient wc = new WebClient();

                wc.DownloadFile(fileParamUri, originalSwaggerFile);
            }

            File.Copy(originalSwaggerFile, modifiedSwaggerFile, true);

            //GET THE FILE UNDER 1 MB

            //Minimise Data Contract Names and References

            dynamic swaggerDoc = JObject.Parse(File.ReadAllText(modifiedSwaggerFile));

            List<string> nonPowerAppsCompatibleEndpoints = new List<string>(new[] { "/api/admin", "/api/reporting/" });

            List<string> sensitiveEndpoints = new List<string>(new[] { "/api/people/{id}/accounts" });

            List<string> listOfEndpointsToKeep = new List<string>(new[] { "/api/admin/system/ListAgencies", "/api/", "/api/Job", "api/Negotiator", "api/people/sendnotification", "/api/inboundlead/create", "/api/featureprovisioning/enrollagency", "api/agency/apikey", "/api/group/addgroup", "/api/job/SendSystemEmail" });

            List<string> listOfEndpointsToRemove = new List<string>(new[] { "/api/admin", "/api/documentgeneration/", "/api/locale/", "/api/chat/", "/api/Job/", "/api/todo", "api/Negotiator/" });


            //Always exclude endpoints that are not compatible with powerapps for some reason
            listOfEndpointsToRemove.AddRange(nonPowerAppsCompatibleEndpoints);
            listOfEndpointsToRemove.AddRange(sensitiveEndpoints);

            AddNewEndpointsAndPaths(swaggerDoc, AddPaths, AddDefinitions);

            RemoveEndpoints(swaggerDoc, listOfEndpointsToKeep, listOfEndpointsToRemove, null, false);

            List<string> remainingPathKeys = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.paths).Keys.ToList();

            RemoveUnreferencedDataContracts(swaggerDoc);

            SetSecurity(swaggerDoc, environmentName, forBusinessUse, useImplicitFlow);

            File.WriteAllText(modifiedSwaggerFile, ((JObject)swaggerDoc).ToString(Newtonsoft.Json.Formatting.None));


            File.WriteAllText(modifiedSwaggerFile, File.ReadAllText(modifiedSwaggerFile).Replace("Dezrez.Core.DataContracts.External.Api.", ""));
            File.WriteAllText(modifiedSwaggerFile, File.ReadAllText(modifiedSwaggerFile).Replace("Specifies which version of the API to call", "Version"));

            return modifiedSwaggerFile;
        }

        private static void AddNewEndpointsAndPaths(dynamic swaggerDoc, Dictionary<string, dynamic> addPaths, Dictionary<string, dynamic> addDefinitions)
        {

            if (addPaths != null)
            {
                var paths = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.paths);
                foreach (var newPathItemKey in addPaths.Keys)
                {
                    paths.Add(newPathItemKey, addPaths[newPathItemKey]);
                }
            }

            if (addDefinitions != null)
            {
                var definitions = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.definitions);
                foreach (var newDefinitionKey in addDefinitions.Keys)
                {
                    definitions.Add(newDefinitionKey, addDefinitions[newDefinitionKey]);
                }
            }

        }

        private static string GetEnvironmentNameFromSwaggerURL(string jsonFileName)
        {
            if (jsonFileName.ToLower().Contains("-dev"))
            {
                return "DEV";
            }

            if (jsonFileName.ToLower().Contains("-systest"))
            {
                return "SYSTEST";
            }

            if (jsonFileName.ToLower().Contains("-uat"))
            {
                return "UAT";
            }

            return "LIVE";
        }

        private static void SetSecurity(dynamic swaggerDoc, string environmentName, bool forBusinessUse, bool useImplicitFlow)
        {
            if (!forBusinessUse)
            {
                //Set the security to be per-user
                swaggerDoc.securityDefinitions = JObject.Parse($@"
            {{		
            ""dezrezOauth2Implicit"": {{
                ""type"": ""oauth2"",
                ""description"": ""OAuth2 Implicit Flow"",
                ""flow"": ""accessCode"",
                ""authorizationUrl"": ""https://dezrez-core-auth-{environmentName.ToLower()}.dezrez.com/Dezrez.Core.Api/oauth/authorize"",
                ""tokenUrl"": ""https://dezrez-core-auth-{environmentName.ToLower()}.dezrez.com/Dezrez.Core.Api/oauth/token"",
                ""scopes"": {{
                                ""impersonate_user"": ""Fully impersonate you""
                            }}
                        }}
            }}");
            }
            else
            {

                if (useImplicitFlow)
                {
                    swaggerDoc.securityDefinitions = JObject.Parse($@"
            {{		
            'dezrezOauth2Implicit': {{
                'type': 'oauth2',
                'description': 'OAuth2 Implicit Flow',
                'flow': 'accessCode',
                'tokenUrl': 'https://dezrez-core-auth-{environmentName.ToLower()}.dezrez.com/Dezrez.Core.Api/oauth/token',
                'authorizationUrl': 'https://dezrez-core-auth-{environmentName.ToLower()}.dezrez.com/Dezrez.Core.Api/oauth/authorize',
                'scopes': {{
                                'impersonate_any_agency': 'Fully impersonate the system account of any agency'
                            }}
                        }}
            }}");
                }
                else
                {
                    swaggerDoc.securityDefinitions = JObject.Parse($@"
            {{		
            'dezrezOauth2Implicit': {{
                'type': 'oauth2',
                'description': 'OAuth2 Implicit Flow',
                'flow': 'application',
                'tokenUrl': 'https://dezrez-core-auth-{environmentName.ToLower()}.dezrez.com/Dezrez.Core.Api/oauth/token',
                'authorizationUrl': 'https://dezrez-core-auth-{environmentName.ToLower()}.dezrez.com/Dezrez.Core.Api/oauth/authorize',
                'scopes': {{
                                'impersonate_any_agency': 'Fully impersonate the system account of any agency'
                            }}
                        }}
            }}");
                }

                //We also need to stipulate that an AgencyID URL (and optional branchID) parameter is now needed on every endpoint.
                //Loop through all current endpoints and add this.

                foreach (var path in swaggerDoc.paths)
                {
                    //var path = swaggerDoc.paths[pathKey];

                    foreach (var verb in path)
                    {

                        foreach (var operation in verb)
                        {
                            if (!((JProperty)operation).Name.StartsWith("x-"))
                            {
                                JArray parameters = operation.Value.parameters as JArray;

                                //Set the security to require the needed scope
                                JArray security = operation.Value.security as JArray;

                                security.Clear();

                                security.Add(JObject.Parse(@"{""dezrezOauth2Implicit"": [""impersonate_any_agency""]}"));

                                if (!parameters.Any(p => ((string)p["name"]).ToLower() == "agencyid"))
                                {
                                    //We need to add the agencyId parameter
                                    parameters.Add(JToken.Parse(@"{""name"": ""agencyId"",""in"": ""query"",""required"": true,""type"": ""integer"", ""format"": ""int64""}"));
                                }

                                if (!parameters.Any(p => ((string)p["name"]).ToLower() == "branchid"))
                                {
                                    //We need to add the agencyId parameter
                                    parameters.Add(JToken.Parse(@"{""name"": ""branchid"",""in"": ""query"",""required"": false,""type"": ""integer"", ""format"": ""int64""}"));
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void RemoveEndpoints(dynamic swaggerDoc, List<string> includeUrlPrefixes, List<string> excludeUrlPrefixes, List<string> includeOperationIdPrefixes, bool removeUnreferencedContracts)
        {
            List<string> removedPaths = new List<string>();
            List<string> operationIdsToRemove = new List<string>();

            List<string> pathKeys = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.paths).Keys.ToList();

            foreach (var pathKey in pathKeys)
            {
                CleansePath((dynamic)swaggerDoc.paths[pathKey]);

                if (!ShouldBeIncluded(includeUrlPrefixes, excludeUrlPrefixes, pathKey))
                {
                    JObject pathObject = swaggerDoc.paths as JObject;
                    Log($"Removing path {pathKey}");
                    pathObject.Property(pathKey).Remove();
                    continue;
                }
            }
        }

        private static void CleansePath(dynamic path)
        {
            string verb = ((JProperty)((JContainer)path).First).Name;

            string currentSummary = path[verb].summary;

            if (currentSummary != null)
            {

                currentSummary = currentSummary.Replace("/", " ");
                currentSummary = currentSummary.Replace("\\", " ");
                currentSummary = currentSummary.Replace("*", " ");
                currentSummary = currentSummary.Replace(".", " ");

                path[verb].summary = currentSummary;
            }

        }

        private static bool ShouldBeIncluded(List<string> includeUrlPrefixes, List<string> excludeUrlPrefixes, string pathKey)
        {

            if (includeUrlPrefixes == null && excludeUrlPrefixes == null)
            {
                return true;
            }

            bool matchesAtLeastOneInclude = includeUrlPrefixes.Any(pf =>
            {
                return pathKey.ToLower().Contains(pf.ToLower());
            });

            bool matchesAtLeastOneExclude = excludeUrlPrefixes.Any(pf =>
            {
                return pathKey.ToLower().Contains(pf.ToLower());
            });

            if (matchesAtLeastOneInclude ^ matchesAtLeastOneExclude || !(matchesAtLeastOneExclude && matchesAtLeastOneInclude))
            {
                return matchesAtLeastOneInclude;
            }
            else
            {
                //The longest pattern that matches wins
                return GetPatternsThatMatchPath(pathKey, includeUrlPrefixes).OrderBy(s => s.Length).First().Length > GetPatternsThatMatchPath(pathKey, excludeUrlPrefixes).OrderBy(s => s.Length).First().Length;
            }
        }

        private static void RemoveUnreferencedDataContracts(dynamic swaggerDoc)
        {
            List<string> pathKeys = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.paths).Keys.ToList();

            HashSet<string> contractReferencesThatAreUsedByEndpoints = GetContractReferences(swaggerDoc, pathKeys);

            //At this point, all the data contract references that are being referenced directly from the endpoint are in the hashedset, referencesToKeep.
            //We now need to recursivley look at those data contracts to see if the reference any other data contracts, to get a complete list of the contracts we need to keep.

            //List<string> definitionKeys = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.definitions).Keys.ToList();

            List<string> contractsReferencedByOtherContracts = new List<string>();

            foreach (var endpointReferencedContract in contractReferencesThatAreUsedByEndpoints)
            {
                contractsReferencedByOtherContracts.AddRange(GetContractReferencesUsedByThisContractReference(endpointReferencedContract, swaggerDoc));
            }

            var allContractsToKeep = contractReferencesThatAreUsedByEndpoints.ToList().Union(contractsReferencedByOtherContracts).Distinct().ToList();


            JObject definitionsObject = swaggerDoc.definitions as JObject;
            List<string> allContractNamesInSwaggerDoc = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)definitionsObject).Keys.ToList();
            foreach (var contractName in allContractNamesInSwaggerDoc)
            {
                if (!allContractsToKeep.Contains(contractName))
                {
                    Log($"Removing {contractName}");
                    definitionsObject.Property(contractName).Remove();

                }
            }
        }

        private static IEnumerable<string> GetContractReferencesUsedByThisContractReference(string endpointReferencedContract, dynamic swaggerDoc, List<string> dontAddContractsInThisList = null)
        {
            dontAddContractsInThisList = dontAddContractsInThisList ?? new List<string>();
            List<string> contractsReferencedByThisContract = new List<string>();
            endpointReferencedContract = endpointReferencedContract.Replace("#/definitions/", "");

            var contract = swaggerDoc.definitions[endpointReferencedContract];

            foreach (var propertyInfo in contract.properties)
            {
                JObject propertyValue = ((JProperty)propertyInfo).Value as JObject;
                //JObject propertyValue = ((JProperty)propertyInfo).Value as JObject;
                if (propertyInfo.Value.type == "object")
                {

                    if (propertyValue["$ref"] != null)
                    {
                        string contractReference = propertyValue["$ref"].ToString();
                        string standardContractReference = contractReference.Replace("#/definitions/", "");
                        if (dontAddContractsInThisList.Contains(standardContractReference)) continue;
                        contractsReferencedByThisContract.Add(standardContractReference);
                        dontAddContractsInThisList.Add(standardContractReference);
                        contractsReferencedByThisContract.AddRange(GetContractReferencesUsedByThisContractReference(contractReference, swaggerDoc, dontAddContractsInThisList));

                        continue;
                    }
                }

                if (propertyInfo.Value.type == "array")
                {
                    JObject arrayItem = propertyInfo.Value.items as JObject;

                    if (arrayItem["$ref"] != null)
                    {
                        string contractReference = arrayItem["$ref"].ToString();
                        string standardContractReference = contractReference.Replace("#/definitions/", "");
                        if (dontAddContractsInThisList.Contains(standardContractReference)) continue;
                        contractsReferencedByThisContract.Add(standardContractReference);
                        dontAddContractsInThisList.Add(standardContractReference);
                        contractsReferencedByThisContract.AddRange(GetContractReferencesUsedByThisContractReference(contractReference, swaggerDoc, dontAddContractsInThisList));

                        continue;
                    }
                }

                if (propertyValue["$ref"] != null)
                {
                    string contractReference = propertyValue["$ref"].ToString();
                    string standardContractReference = contractReference.Replace("#/definitions/", "");
                    if (dontAddContractsInThisList.Contains(standardContractReference)) continue;
                    contractsReferencedByThisContract.Add(standardContractReference);
                    dontAddContractsInThisList.Add(standardContractReference);
                    contractsReferencedByThisContract.AddRange(GetContractReferencesUsedByThisContractReference(contractReference, swaggerDoc, dontAddContractsInThisList));

                    continue;
                }

            }

            return contractsReferencedByThisContract;
        }

        private static HashSet<string> GetContractReferences(dynamic swaggerDoc, List<string> pathKeys)
        {
            HashSet<string> referencesToKeep = new HashSet<string>();

            foreach (var pathKey in pathKeys)
            {
                var pathItem = swaggerDoc.paths[pathKey];

                foreach (var verbKey in ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)pathItem).Keys)//.Where(k => !k.StartsWith("x-")))
                {
                    var operation = pathItem[verbKey];
                    if (verbKey == "x-ms-notification-content")
                    {
                        if (operation["schema"] != null)
                        {
                            JObject schemaObject = operation.schema as JObject;
                            if (schemaObject["$ref"] != null)
                            {
                                string contractReference = schemaObject["$ref"].ToString();
                                try
                                {
                                    referencesToKeep.Add(contractReference.Replace("#/definitions/", ""));
                                }
                                catch { }
                            }

                            if ((string)operation.schema.type == "array")
                            {
                                JObject arrayItem = operation.schema.items as JObject;

                                if (arrayItem["$ref"] != null)
                                {
                                    referencesToKeep.Add(arrayItem["$ref"].ToString().Replace("#/definitions/", ""));
                                }
                            }
                        }
                    }
                    else
                    {
                        
                        string operationId = operation.operationId;
                        foreach (var parameter in operation.parameters)
                        {
                            if (parameter["schema"] != null)
                            {
                                JObject schemaObject = parameter.schema as JObject;
                                if (schemaObject["$ref"] != null)
                                {
                                    string contractReference = schemaObject["$ref"].ToString();
                                    try
                                    {
                                        referencesToKeep.Add(contractReference.Replace("#/definitions/", ""));
                                    }
                                    catch { }
                                }

                                if ((string)parameter.schema.type == "array")
                                {
                                    JObject arrayItem = parameter.schema.items as JObject;

                                    if (arrayItem["$ref"] != null)
                                    {
                                        referencesToKeep.Add(arrayItem["$ref"].ToString().Replace("#/definitions/", ""));
                                    }
                                }
                            }

                        }
                        //Responses collection may contain references to needed data contracts
                        foreach (var operationResponse in operation.responses)
                        {
                            if (operationResponse.Value.schema != null)
                            {
                                string contractReference = null;
                                JObject propertyValue = null;
                                switch ((string)operationResponse.Value.schema.type)
                                {
                                    case "object":
                                        propertyValue = operationResponse.Value.schema as JObject;

                                        if (propertyValue["$ref"] != null)
                                        {
                                            contractReference = propertyValue["$ref"].ToString();
                                        }
                                        break;

                                    case "array":
                                        JObject arrayItem = operationResponse.Value.schema.items as JObject;

                                        if (arrayItem["$ref"] != null)
                                        {
                                            contractReference = arrayItem["$ref"].ToString();
                                        }
                                        break;
                                    default:


                                        propertyValue = operationResponse.Value.schema as JObject;
                                        if (propertyValue["$ref"] != null)
                                        {
                                            contractReference = propertyValue["$ref"].ToString();
                                        }
                                        break;

                                }

                                if (contractReference != null)
                                {
                                    referencesToKeep.Add(contractReference.Replace("#/definitions/", ""));
                                }

                            }

                        }
                    }
                }
            }

            return referencesToKeep;
        }

        private static bool ContainsAny(string pathKey, List<string> includeUrlPrefixes)
        {
            bool match = includeUrlPrefixes.Any(pf =>
            {
                return pathKey.ToLower().Contains(pf.ToLower());
            });

            return match;
        }

        private static IEnumerable<string> GetPatternsThatMatchPath(string pathKey, List<string> patterns)
        {
            return patterns.Where(pf =>
            {
                return pathKey.ToLower().Contains(pf.ToLower());
            });
        }

        /*
         * 
         * 
         * "securityDefinitions": {
		"oauth2": {
			"type": "oauth2",
			"description": "OAuth2 Implicit Flow",
			"flow": "accessCode",
			"authorizationUrl": "https://dezrez-core-auth-dev.dezrez.com/Dezrez.Core.Api/oauth/authorize",
			"tokenUrl": "https://dezrez-core-auth-dev.dezrez.com/Dezrez.Core.Api/oauth/token",
			"scopes": {
				"impersonate_user": "Fully impersonate you"
			}
		}
	}
         */
    }
    public static class ExtensionMethodsList
    {
        public static bool IsGenericList(this Type o)
        {
            return (o.IsGenericType && (o.GetGenericTypeDefinition() == typeof(List<>)));
        }
    }
}
