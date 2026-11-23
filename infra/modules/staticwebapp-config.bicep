@description('Static Web App name')
param staticWebAppName string

@description('Functions App default hostname for backend API calls')
param functionsDefaultHostname string

@description('Functions App name used to resolve its host key after deployment')
param functionsAppName string

@description('Application Insights connection string for SWA')
param appInsightsConnectionString string

resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' existing = {
  name: staticWebAppName
}

resource staticWebAppConfig 'Microsoft.Web/staticSites/config@2023-01-01' = {
  parent: staticWebApp
  name: 'appsettings'
  properties: {
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    BACKEND_FUNCTIONS_BASE_URL: 'https://${functionsDefaultHostname}'
    BACKEND_FUNCTIONS_KEY: listKeys('${resourceId('Microsoft.Web/sites', functionsAppName)}/host/default', '2023-12-01').functionKeys.default
  }
}

output configName string = staticWebAppConfig.name
