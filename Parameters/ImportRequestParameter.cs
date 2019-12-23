using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using SBA.Durable.Parameters;

namespace SBA.Durable
{
    public class ImportRequestParameter
    {

        const string ConnectionString = "AzureWebJobsStorage";
        private HttpRequestMessage requestInput;

        public ImportRequestParameter(HttpRequestMessage requestInput)
        {
            this.requestInput = requestInput;
        }
        public async Task<string> GenerateInstanceID()
        {
            var nvc = requestInput.RequestUri.ParseQueryString();
            if (nvc.AllKeys.Count(_ => _ == "instance") > 0)
            {
                return nvc["instance"].ToString();
            }
            return Guid.NewGuid().ToString("N");
        }
        public async Task<List<ImportParameters>> UploadFilesToStorageAccountAndGnerateParameters()
        {
            List<ImportParameters> returnValue = new List<ImportParameters>();
            MultipartMemoryStreamProvider inputStreams = await requestInput.Content.ReadAsMultipartAsync();
            Dictionary<string, byte[]> fileData = inputStreams.ExtractStreams();
            var nvc = requestInput.RequestUri.ParseQueryString();

            string halbJahrParam = nvc["Halbjahr"]?.ToString();
            halbJahrParam = System.Web.HttpUtility.UrlDecode(halbJahrParam);

            string constring = Environment.GetEnvironmentVariable(ConnectionString);

            BlobServiceClient blobServiceClient = new BlobServiceClient(constring);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("imports");

            string instanceId = string.Empty;
            foreach (KeyValuePair<string, byte[]> kvp in fileData)
            {
                ImportParameters parameter = new ImportParameters();
                parameter.FileName = string.Concat(kvp.Key, "_", Guid.NewGuid().ToString("N"));
                parameter.HalfYearSetting = halbJahrParam;
                BlobClient blob = containerClient.GetBlobClient(parameter.FileName);
                blob.Upload(new MemoryStream(kvp.Value));
                returnValue.Add(parameter);
            }
            return returnValue;
        }

    }
}