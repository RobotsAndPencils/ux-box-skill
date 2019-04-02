using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Generic;

namespace UXSkill {
    public static class ElasticAllenComponent {
        private static readonly Configuration config = Configuration.GetInstance.Result;
        private static readonly HttpClient client = new HttpClient();


        public static async Task IndexMetadata (Conversation convo, string fileUrl) {
            if (config.ElasticAllenEnabled) {
                string body = convertConversationForAE(convo, fileUrl);
                Console.WriteLine("==== EA Package =======");
                Console.WriteLine(body);
                var response = await client.PostAsync(config.ElasticAllenUrl, new StringContent(body, Encoding.UTF8, "application/json"));
                Console.WriteLine(JsonConvert.SerializeObject(response));
                if (response.StatusCode != System.Net.HttpStatusCode.Created || response.StatusCode != System.Net.HttpStatusCode.OK) {
                    Console.WriteLine($"Data not injeested by AE: {response.StatusCode}");
                }
            }
        }

        private static String convertConversationForAE (Conversation convo, string fileUrl) {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            foreach (var speakerResult in convo.resultByTime) {
                var entry = new Dictionary<string, object>() {
                    { "type", "transcript" },
                    { "text", speakerResult.text },
                    { "speaker", convo.speakerLabels[speakerResult.speaker] },
                    { "timeStart", speakerResult.start },
                    { "timeEnd", speakerResult.end }
                };
                items.Add(entry);
            }
            var request = new Dictionary<string, object>() {
                { "channel", "CG6BUQG5D" },
                { "token" , "aaede76cd9e447371fe8fea83ea56445" },
                { "url" , fileUrl },
                { "items", items }
            };

            return JsonConvert.SerializeObject(request);
        }
    }
}
