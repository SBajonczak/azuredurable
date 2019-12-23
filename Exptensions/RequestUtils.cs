using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SBA.Durable
{
    public static class RequestUtils
    {
        /// <summary>
        /// Transform the Requestheaders to a dictionary
        /// </summary>
        /// <param name="headers"></param>
        /// <returns></returns>
        public static Dictionary<string, object> MapHeadersToDictionary(this HttpRequestHeaders headers)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            Parallel.ForEach(headers, (header) =>
            {
                string keyname = header.Key;
                if (properties.ContainsKey(keyname) == false)
                {
                    if (!string.IsNullOrEmpty(header.Value.FirstOrDefault()))
                    {
                        // Decoding Data.
                        string data = header.Value.FirstOrDefault();
                        properties.Add(keyname, data);
                    }
                }
            }
            );
            return properties;
        }
        private static object lockobjectMultipartUpload = new object();

        /// <summary>
        /// Extract the Filestream from the Multipart stream.
        /// </summary>
        /// <param name="streamprovider"></param>
        /// <returns></returns>
        public static Dictionary<string, byte[]> ExtractStreams(this MultipartMemoryStreamProvider streamprovider)
        {
            Dictionary<string, byte[]> resultValue = new Dictionary<string, byte[]>();
            foreach (HttpContent content in streamprovider.Contents)
            {
                lock (lockobjectMultipartUpload)
                {
                    string fileName = content.Headers.ContentDisposition.FileName.Replace("\"", "");
                    Task<byte[]> fileContents = content.ReadAsByteArrayAsync();
                    fileContents.Wait();
                    resultValue.Add(fileName, fileContents.Result);
                }
            }
            return resultValue;
        }
    }
}