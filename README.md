# Dezrez Core API PowerApps Swagger Parser
This tool *mangles* the default swagger file so that it works with Microsoft's PowerApps service.

In theory, the default swagger that is generated should work, apart from the following issues:

*The default swagger file is over the 1MB limit that Microsoft PowerApps imposes during Custom Connector creation in PowerApps.
*The Swagger 2 spec does not handle optional URL parameters very well, and these need to be stripped out by hand every time.
*Most of the time, you dont need to use every single endpoint - This tool allows you to exclude some endpoints, and remove unreferenced data contracts as a result.

## How to use this tool
Download this and build it

Specify the URL to the swagger file as a command line parameter

The console app will then show the path to the modified file, upload this to the PowerApps service.

You'll need a clientID and secret in order to communicate with the Dezrez Core API, and you can register for one on https://developer.dezrez.com.

Im thinking about publishing this as a web service for easier access.
