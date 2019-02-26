using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Amazon.S3.Model;
using System.Net;
using Amazon.S3;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace UXSkill {
    public class Function {
        private static Configuration config = Configuration.GetInstance.Result;

        public async Task FunctionHandler(System.IO.Stream request, ILambdaContext context) {
            Console.WriteLine("======== Skill Executing!! =========");

            string requestStr;
            using (StreamReader reader = new StreamReader(request)) {
                requestStr = reader.ReadToEnd();
            }
            dynamic requestJson = JObject.Parse(requestStr);
            dynamic inputJson = JObject.Parse(requestJson.body.Value);
            string fileId = inputJson.source.id.Value;
            Console.WriteLine("======== Request String =========");
            Console.WriteLine(requestStr);
            Console.WriteLine("======== Context =========");
            Console.WriteLine(JsonConvert.SerializeObject(context, Formatting.None));
            Console.WriteLine("======== Box Input (body) =========");
            Console.WriteLine(JsonConvert.SerializeObject(inputJson, Formatting.None));
            var jobName = $"f{inputJson.source.id}";

            // move file to S3 for processing (aws can not process using anything other than an S3 uir)
            var fileUrl = BoxHelper.getFileUrl(fileId, inputJson.token);
            Console.WriteLine($"FileUrl: {fileUrl}");
            string fileExt = Path.GetExtension(inputJson.source.name.Value).TrimStart('.');
            string fileName = $"{jobName}.{fileExt}";
            string mimeType = MimeMapping.GetMimeType(fileExt);

            PutObjectResponse response = await BoxHelper.UploadBoxFileToS3(fileUrl, config.S3BucketName, mimeType, fileName);
            Console.WriteLine("======== Put Object Response =========");
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.None));
            if (response.HttpStatusCode.CompareTo(HttpStatusCode.OK) != 0) {
                throw new Exception("Status code error");
            }

            Console.WriteLine("JobName: " + jobName);
            Conversation convo = await AWSHelper.TranscribeMedia(jobName, fileName, fileExt);

            await AWSHelper.DeleteObjectNonVersionedBucketAsync(fileName);
            //await BoxHelper.GenerateCards(result, inputJson);
            await GenerateCards(convo, inputJson.token.write.access_token.Value, fileId);

        }

        public static async Task GenerateCards(Conversation convo, string writeToken, string fileId) {
            var cards = new List<Dictionary<string, object>>();
            cards.AddRange(BoxHelper.GenerateTranscriptCard(convo, fileId));
            cards.AddRange(BoxHelper.GenerateTopicsKeywordCard(convo, fileId));
            await BoxHelper.UpdateSkillCards(cards, writeToken, fileId);
        }
    }
}
