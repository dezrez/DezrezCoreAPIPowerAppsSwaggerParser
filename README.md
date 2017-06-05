# Dezrez Core API PowerApps Swagger Parser
This tool *mangles* the default swagger file so that it works with Microsoft's PowerApps service.

In theory, the default swagger that is generated should work, apart from the following issues:

*The default swagger file is over the 1MB limit that Microsoft PowerApps imposes during Custom Connector creation in PowerApps.
*The Swagger 2 spec does not handle optional URL parameters very well, and these need to be stripped out by hand every time.
*Most of the time, you dont need to use every single endpoint - This tool allows you to exclude some endpoints, and remove unreferenced data contracts as a result.

