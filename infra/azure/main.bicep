targetScope = 'resourceGroup'

@description('Azure region for all resources. Defaults to the current resource group location.')
param location string = resourceGroup().location

@description('Base name for the deployment environment.')
param environmentName string = 'mcpnet'

@description('Unique Azure Container Registry name.')
param containerRegistryName string

@description('Deploy only shared infrastructure when false. Used by the deployment wrapper before images are built.')
param deployContainerApps bool = true

@description('Image tag to deploy for all backend services.')
param imageTag string = 'latest'

@description('Container repository for McpHost.')
param mcpHostImageRepository string = 'mcp-host'

@description('Container repository for UwMcp.')
param uwMcpImageRepository string = 'uw-mcp'

@description('Container repository for DevOpsMcp.')
param devOpsMcpImageRepository string = 'devops-mcp'

@description('Public origin for the separately hosted MyChat UI.')
param myChatCorsOrigin string

@allowed([
  'openai'
  'gemini'
])
param llmProvider string = 'openai'

param llmModel string = ''
param openAiModel string = 'gpt-4.1-mini'
param geminiModel string = 'gemini-2.5-flash'
param llmReasoningEffort string = 'medium'
param sessionTtlSecs int = 3600
param systemInstruction string = ''

@secure()
param openAiApiKey string = ''

@secure()
param geminiApiKey string = ''

param apimBaseUrl string
param submissionUrl string
param quoteUrl string
param apimSubscriptionKey string = 'Ocp-Apim-Subscription-Key'

@secure()
param apimSubscriptionKeyValue string

param tfsBaseUrl string

@secure()
param tfsPat string

param logAnalyticsWorkspaceName string = '${environmentName}-logs'
param containerAppEnvironmentName string = '${environmentName}-aca-env'
param mcpHostAppName string = 'mcp-host'
param uwMcpAppName string = 'uw-mcp'
param devOpsMcpAppName string = 'devops-mcp'

var acrCredentials = acr.listCredentials()
var acrLoginServer = acr.properties.loginServer
var uwMcpInternalUrl = 'http://${uwMcpAppName}/mcp'
var devOpsMcpInternalUrl = 'http://${devOpsMcpAppName}/mcp'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      disableLocalAuth: false
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
    publicNetworkAccess: 'Enabled'
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource uwMcpApp 'Microsoft.App/containerApps@2024-03-01' = if (deployContainerApps) {
  name: uwMcpAppName
  location: location
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8001
        transport: 'auto'
        allowInsecure: true
      }
      registries: [
        {
          server: acrLoginServer
          username: acrCredentials.username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acrCredentials.passwords[0].value
        }
        {
          name: 'apim-subscription-key-value'
          value: apimSubscriptionKeyValue
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'uw-mcp'
          image: '${acrLoginServer}/${uwMcpImageRepository}:${imageTag}'
          env: [
            {
              name: 'PORT'
              value: '8001'
            }
            {
              name: 'MCP_HTTP_HOST'
              value: '0.0.0.0'
            }
            {
              name: 'MCP_HTTP_PATH'
              value: '/mcp'
            }
            {
              name: 'APIM_BASE_URL'
              value: apimBaseUrl
            }
            {
              name: 'SUBMISSION_URL'
              value: submissionUrl
            }
            {
              name: 'QUOTE_URL'
              value: quoteUrl
            }
            {
              name: 'APIM_SUBSCRIPTION_KEY'
              value: apimSubscriptionKey
            }
            {
              name: 'APIM_SUBSCRIPTION_KEY_VALUE'
              secretRef: 'apim-subscription-key-value'
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

resource devOpsMcpApp 'Microsoft.App/containerApps@2024-03-01' = if (deployContainerApps) {
  name: devOpsMcpAppName
  location: location
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8002
        transport: 'auto'
        allowInsecure: true
      }
      registries: [
        {
          server: acrLoginServer
          username: acrCredentials.username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acrCredentials.passwords[0].value
        }
        {
          name: 'tfs-pat'
          value: tfsPat
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'devops-mcp'
          image: '${acrLoginServer}/${devOpsMcpImageRepository}:${imageTag}'
          env: [
            {
              name: 'PORT'
              value: '8002'
            }
            {
              name: 'MCP_HTTP_HOST'
              value: '0.0.0.0'
            }
            {
              name: 'MCP_HTTP_PATH'
              value: '/mcp'
            }
            {
              name: 'TFS_BASE_URL'
              value: tfsBaseUrl
            }
            {
              name: 'TFS_PAT'
              secretRef: 'tfs-pat'
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

resource mcpHostApp 'Microsoft.App/containerApps@2024-03-01' = if (deployContainerApps) {
  name: mcpHostAppName
  location: location
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8888
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: acrLoginServer
          username: acrCredentials.username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acrCredentials.passwords[0].value
        }
        {
          name: 'openai-api-key'
          value: openAiApiKey
        }
        {
          name: 'gemini-api-key'
          value: geminiApiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcp-host'
          image: '${acrLoginServer}/${mcpHostImageRepository}:${imageTag}'
          env: [
            {
              name: 'API_PORT'
              value: '8888'
            }
            {
              name: 'CORS_ORIGINS'
              value: myChatCorsOrigin
            }
            {
              name: 'LLM_PROVIDER'
              value: llmProvider
            }
            {
              name: 'LLM_MODEL'
              value: llmModel
            }
            {
              name: 'OPENAI_MODEL'
              value: openAiModel
            }
            {
              name: 'GEMINI_MODEL'
              value: geminiModel
            }
            {
              name: 'LLM_REASONING_EFFORT'
              value: llmReasoningEffort
            }
            {
              name: 'SESSION_TTL_SECS'
              value: string(sessionTtlSecs)
            }
            {
              name: 'SYSTEM_INSTRUCTION'
              value: systemInstruction
            }
            {
              name: 'UW_SERVER_URL'
              value: uwMcpInternalUrl
            }
            {
              name: 'DEVOPS_SERVER_URL'
              value: devOpsMcpInternalUrl
            }
            {
              name: 'OPENAI_API_KEY'
              secretRef: 'openai-api-key'
            }
            {
              name: 'GEMINI_API_KEY'
              secretRef: 'gemini-api-key'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output acrName string = acr.name
output acrLoginServer string = acrLoginServer
output mcpHostImageRepository string = mcpHostImageRepository
output uwMcpImageRepository string = uwMcpImageRepository
output devOpsMcpImageRepository string = devOpsMcpImageRepository
output containerAppEnvironmentDefaultDomain string = managedEnvironment.properties.defaultDomain
output mcpHostUrl string = deployContainerApps ? 'https://${mcpHostApp!.properties.configuration.ingress.fqdn}' : ''
