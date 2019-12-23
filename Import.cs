using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SBA.Durable.Parameters;

namespace SBA.Durable
{
    public static class Import
    {
        [FunctionName("Import")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            ImportParametersContainer data = context.GetInput<ImportParametersContainer>();
            var outputs = new List<string>();
            foreach (var importParmeter in data.Importings)
            {
                outputs.Add(await context.CallActivityAsync<string>("Import_FetchFile", importParmeter));
            }
            outputs.Add(await context.CallActivityAsync<string>("Import_SendNotification", data));
            return outputs;

        }

        [FunctionName("Import_FetchFile")]

        public static async Task<string> FetchFile([ActivityTrigger] ImportParameters parameter, ILogger log)
        {
            string constring = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            BlobServiceClient blobServiceClient = new BlobServiceClient(constring);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("imports");
            BlobClient blob = containerClient.GetBlobClient(parameter.FileName);

            MemoryStream s = new MemoryStream();
            blob.DownloadTo(s);
            parameter.Content = s.ToArray();
            // Add Logic for Importing and replacing the following line
            List<ImportResult> returnValue= new List<ImportResult>();
            // Return the results
            return JsonConvert.SerializeObject(returnValue);

        }

        [FunctionName("Import_SendNotification")]

        public static async Task<string> SendNotification([ActivityTrigger] ImportParametersContainer parameter, ILogger log)
        {
            log.LogInformation("Sending information to requester");
            // Add Mail Logic here!
            return $"Mail to {parameter.NotifierMail} was send";
        }

        [FunctionName("Import_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ClaimsPrincipal principal,
            ILogger log)
        {


            ImportRequestParameter requestParameter = new ImportRequestParameter(req);
            List<ImportParameters> result = await requestParameter.UploadFilesToStorageAccountAndGnerateParameters();

            ImportParametersContainer parametersContainer = new ImportParametersContainer(result, "");
            // Get Username
            string customInstanceID= ((ClaimsIdentity)principal.Identity).Claims.First().Value;
            // Set id to instance id
            parametersContainer.InstanceId = customInstanceID;
            string instanceId = await starter.StartNewAsync("Import", parametersContainer.InstanceId,  parametersContainer);



            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}