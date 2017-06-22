using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ReziSwaggerPowerAppsParser
{
    class Program
    {

        static void Main(string[] args)
        {
            bool forBusinessUse = true;//Signifies if we'll be able to impersonate all agencies etc.

            bool useImplicitFlow = true;//WHen forBusinessUse = true, specifies to use implicitFlow anyway, instead of using client flow (as client flow doesnt work yet in PowerApps)

            string jsonFileName = args[0];

            string modifiedSwaggerFile = MangleSwagger(forBusinessUse, useImplicitFlow, jsonFileName);

            FileInfo finfo = new FileInfo(modifiedSwaggerFile);

            Console.WriteLine($"{modifiedSwaggerFile} {finfo.Length} bytes in size");

            Console.ReadKey();
        }

        private static string MangleSwagger(bool forBusinessUse, bool useImplicitFlow, string jsonFileName)
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

            List<string> listOfEndpointsToKeep = new List<string>(new[] { "/api/admin/system/ListAgencies", "/api/Job", "api/Negotiator", "api/people/sendnotification", "/api/inboundlead/create", "/api/featureprovisioning/enrollagency", "api/agency/apikey", "/api/group/addgroup", "/api/job/SendSystemEmail" });

            List<string> listOfEndpointsToRemove = new List<string>(new[] { "/api/admin", "/api/documentgeneration/", "/api/locale/", "/api/chat/", "/api/Job/", "/api/todo", "api/Negotiator/" });

            //Always exclude endpoints that are not compatible with powerapps for some reason
            listOfEndpointsToRemove.AddRange(nonPowerAppsCompatibleEndpoints);

            RemoveEndpoints(swaggerDoc, listOfEndpointsToKeep, listOfEndpointsToRemove, null, false);
            List<string> remainingPathKeys = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.paths).Keys.ToList();

            RemoveUnreferencedDataContracts(swaggerDoc);

            SetSecurity(swaggerDoc, environmentName, forBusinessUse, useImplicitFlow);

            File.WriteAllText(modifiedSwaggerFile, ((JObject)swaggerDoc).ToString(Newtonsoft.Json.Formatting.None));


            File.WriteAllText(modifiedSwaggerFile, File.ReadAllText(modifiedSwaggerFile).Replace("Dezrez.Core.DataContracts.External.Api.", ""));
            File.WriteAllText(modifiedSwaggerFile, File.ReadAllText(modifiedSwaggerFile).Replace("Specifies which version of the API to call", "Version"));

            return modifiedSwaggerFile;
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

            List<string> definitionKeys = ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)swaggerDoc.definitions).Keys.ToList();

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
                    definitionsObject.Property(contractName).Remove();

                }
            }
        }

        private static IEnumerable<string> GetContractReferencesUsedByThisContractReference(string endpointReferencedContract, dynamic swaggerDoc)
        {
            List<string> contractsReferencedByOtherContract = new List<string>();
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
                        contractsReferencedByOtherContract.Add(contractReference.Replace("#/definitions/", ""));
                        contractsReferencedByOtherContract.AddRange(GetContractReferencesUsedByThisContractReference(contractReference, swaggerDoc));
                        continue;
                    }
                }

                if (propertyInfo.Value.type == "array")
                {
                    JObject arrayItem = propertyInfo.Value.items as JObject;

                    if (arrayItem["$ref"] != null)
                    {
                        string contractReference = arrayItem["$ref"].ToString();
                        contractsReferencedByOtherContract.Add(contractReference.Replace("#/definitions/", ""));
                        contractsReferencedByOtherContract.AddRange(GetContractReferencesUsedByThisContractReference(contractReference, swaggerDoc));
                        continue;
                    }
                }

                if (propertyValue["$ref"] != null)
                {
                    string contractReference = propertyValue["$ref"].ToString();
                    contractsReferencedByOtherContract.Add(contractReference.Replace("#/definitions/", ""));
                    contractsReferencedByOtherContract.AddRange(GetContractReferencesUsedByThisContractReference(contractReference, swaggerDoc));
                    continue;
                }

            }

            return contractsReferencedByOtherContract;
        }

        private static HashSet<string> GetContractReferences(dynamic swaggerDoc, List<string> pathKeys)
        {
            HashSet<string> referencesToKeep = new HashSet<string>();

            foreach (var pathKey in pathKeys)
            {
                var pathItem = swaggerDoc.paths[pathKey];

                foreach (var verbKey in ((System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken>)pathItem).Keys)
                {
                    var operation = pathItem[verbKey];
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
}
