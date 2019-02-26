using Box.V2;
using Box.V2.Auth;
using Box.V2.Config;
using Box.V2.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Amazon.Comprehend.Model;
using System.Text.RegularExpressions;
using Amazon.TranscribeService.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using System.Net;
using System.IO;

namespace UXSkill {
    public static class BoxHelper {
        private enum SkillType { timeline, keyword, transcript };
        private static readonly Configuration config = Configuration.GetInstance.Result;
        private static Random random = new Random();
        public static string getFileUrl(string id, dynamic token) {
            return $"{config.BoxApiUrl}/files/{id}/content?access_token={token.read.access_token}";
        }
        // fileId comes from boxBody.source.id.Value
        // writeToken comes from boxBody.token.write.access_token.Value
        public static async Task UpdateSkillCards(List<Dictionary<string, object>> cards, string writeToken, string fileId) {
            //var boxConfig = new BoxConfig(string.Empty, string.Empty, new Uri(config.BoxApiUrl));
            //var session = new OAuthSession(writeToken, string.Empty, 3600, "bearer");
            //var client = new BoxClient(boxConfig, session);
            var boxConfig = new BoxConfig(string.Empty, string.Empty, new Uri(config.BoxSdkUrl));
            var session = new OAuthSession(writeToken, string.Empty, 3600, "bearer");
            var client = new BoxClient(boxConfig, session);
            //var currentUser = await client.UsersManager.GetCurrentUserInformationAsync();

            if (client == null) {
                throw new Exception("Unable to create box client");
            }
            var skillsMetadata = new Dictionary<string, object>(){
                { "cards", cards }
            };

            //TODO: first load then merge (so it's an update and not replacement off all that's there)
            try {
                Console.WriteLine("--------------------- Cards --------------");
                Console.WriteLine(JsonConvert.SerializeObject(skillsMetadata, Formatting.None));
                await client.MetadataManager.CreateFileMetadataAsync(fileId, skillsMetadata, "global", "boxSkillsCards");
                Console.WriteLine("Created metadata");
            } catch (Exception e) {
                Console.WriteLine("Exception creating metadata. Trying update");
                Console.WriteLine(e);
                BoxMetadataUpdate updateObj = new BoxMetadataUpdate {
                    Op = MetadataUpdateOp.replace,
                    Path = "/cards",
                    Value = cards
                };
                try {
                    await client.MetadataManager.UpdateFileMetadataAsync(fileId, new List<BoxMetadataUpdate>() { updateObj }, "global", "boxSkillsCards");
                } catch (Exception e2) {
                    Console.WriteLine("Exception updating metadata. giving up");
                    Console.WriteLine(e2);
                    return;
                }
                Console.WriteLine("Successfully updated metadata");
            }
        }

        public static List<Dictionary<string, object>> GenerateTranscriptCard(Conversation convo, string fileId) {
            var card = GetSkillCardTemplate(SkillType.transcript, fileId, "Transcript", convo.duration);
            foreach (var speakerResult in convo.resultByTime) {
                var entry = new Dictionary<string, object>() {
                    { "type", "text" },
                    { "text", $"[{convo.speakerLabels[speakerResult.speaker]}]  {speakerResult.text}" },
                    { "appears", new List<Dictionary<string, object>>() {
                        new Dictionary<string, object>() {
                            { "start", speakerResult.start },
                            { "end", speakerResult.end }
                        }
                    } }
                };

                ((List<Dictionary<string, object>>)card["entries"]).Add(entry);
            }
            return new List<Dictionary<string, object>> { card };
        }

        //Run through results, grouping words and saving the locations in the media file. Create card with top 20
        //words more than 5 characters. 
        // TODO: should have common word list to ignore instead of <5 chars
        // TODO: should calculate proximity to find phrases that appear together
        public static List<Dictionary<string, object>> GenerateTopicsKeywordCard(Conversation convo, string fileId) {
            var card = GetSkillCardTemplate(SkillType.keyword, fileId, "Topics", convo.duration);
            var topics = new List<string>(convo.topicLocations.Keys);
            var count = 0;

            topics.Sort(delegate (string a, string b) {
                return convo.topicLocations[a].Count.CompareTo(convo.topicLocations[b].Count);
            });

            foreach (var topic in topics) {
                if (count++ == 20) break;
                var entry = new Dictionary<string, object>() {
                    { "type", "text" },
                    { "text", topic },
                    { "appears", new List<Dictionary<string, object>>() }
                };

                foreach (var speakerResult in convo.topicLocations[topic]) {
                    var location = new Dictionary<string, object>() {
                        { "start", speakerResult.start },
                        { "end", speakerResult.end }
                    };
                    ((List<Dictionary<string, object>>)entry["appears"]).Add(location);
                }
                ((List<Dictionary<string, object>>)card["entries"]).Add(entry);
            }

            return new List<Dictionary<string, object>> { card };
        }

        private static Dictionary<string, object> GetSkillCardTemplate(SkillType type, string fileId, string title, decimal duration) {
            var template = new Dictionary<string, object>() {
                { "type", "skill_card" },
                { "skill_card_type", type.ToString() },
                { "skill", new Dictionary<string, object>() {
                        { "type", "service" },
                        { "id", $"{title.Replace(" ","")}_{fileId}" }
                }},
                { "invocation", new Dictionary<string, object>() {
                        { "type", "skill_invocation" },
                        { "id", $"I{fileId}" }
                }},
                { "skill_card_title", new Dictionary<string, object>() {
                        { "message", title }
                }},
                { "duration", duration },
                { "entries",  new List<Dictionary<string, object>>() }
            };

            return template;
        }
        private static Dictionary<string, object> AddBasicEntries(Dictionary<string, object> card, String[] entryStrings) {
            foreach (var str in entryStrings) {
                var entry = new Dictionary<string, object>() {
                    { "type", "text" },
                    { "text", str }
                };
                ((List<Dictionary<string, object>>)card["entries"]).Add(entry);
            }
            return card;
        }

        public static async Task<PutObjectResponse> UploadBoxFileToS3(string url, string bucketName, string mimeType, string key) {
            WebRequest req = WebRequest.Create(url);
            WebResponse response = req.GetResponse();
            Stream responseStream = response.GetResponseStream();

            MemoryStream contentStream;
            using (var localStream = new MemoryStream()) {
                byte[] buffer = new byte[2048]; // read in chunks of 2KB
                int bytesRead;
                while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0) {
                    localStream.Write(buffer, 0, bytesRead);
                }
                byte[] fileContent = localStream.ToArray();
                contentStream = new MemoryStream(fileContent);
            }


            PutObjectRequest request = new PutObjectRequest {
                BucketName = bucketName,
                ContentType = mimeType,
                Key = key,
                InputStream = contentStream
            };
            var result = await config.S3Client.PutObjectAsync(request);
            return result;
        }

    }

}

