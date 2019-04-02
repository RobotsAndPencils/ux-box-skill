using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.Comprehend;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.TranscribeService;
using Newtonsoft.Json.Linq;

namespace UXSkill {
    public class Configuration {
        public string S3Region { get; set; }
        public string S3BucketName { get; set; }
        public string S3ConfigKey { get; set; }
        public string Environment { get; set; }
        public IAmazonS3 S3Client { get; }
        public AmazonTranscribeServiceClient AzTranscribeClient { get; }
        public AmazonComprehendClient AzComprehendClient { get; }
        public string BoxApiUrl { get; set; }
        public string BoxSdkUrl { get; set; }
        public string ElasticAllenUrl { get; set; }
        public bool ElasticAllenEnabled { get; set; }
        public int MaxSpeakerLabels { get; set; }
        public string Language { get; set; }

        // enough configuration is kept in the AWS environment to pull up the real config file in s3
        public Configuration() {
            this.S3Region = System.Environment.GetEnvironmentVariable("AWS_S3_REGION");
            this.S3BucketName = System.Environment.GetEnvironmentVariable("AWS_S3_BUCKET");
            this.S3ConfigKey = System.Environment.GetEnvironmentVariable("AWS_S3_CONFIGKEY");
            this.Environment = System.Environment.GetEnvironmentVariable("ENVIRONMENT");
            var regionEndpoint = RegionEndpoint.GetBySystemName(S3Region);
            this.S3Client = new AmazonS3Client(regionEndpoint);
            this.AzTranscribeClient = new AmazonTranscribeServiceClient(regionEndpoint);
            this.AzComprehendClient = new AmazonComprehendClient(regionEndpoint);
        }

        public static Task<Configuration> GetInstance { get; } = CreateSingleton();

        private static async Task<Configuration> CreateSingleton() {
            var instance = new Configuration();
            await instance.InitializeAsync();
            return instance;
        }

        // get the config file from s3 and initialize properties
        public async Task InitializeAsync() {
            string configKey = $"{Environment}_{S3ConfigKey}";

            try {
                string env = await GetS3FileContent(S3BucketName, configKey);
                dynamic envJson = JObject.Parse(env);

                // Initialize json sourced properties
                this.BoxApiUrl = envJson.box.apiUrl.Value;
                this.MaxSpeakerLabels = (int)envJson.aws.transcription.maxSpeakerLabels.Value;
                this.Language = envJson.aws.language.Value;
                this.BoxSdkUrl = envJson.box.sdkUrl.Value;
                this.ElasticAllenUrl = envJson.elasticAllen.apiUrl.Value;
                this.ElasticAllenEnabled = envJson.elasticAllen.enabled.Value;
                Console.WriteLine($"BoxApiUrl: {BoxApiUrl}");
            } catch (AmazonS3Exception e) {
                Console.WriteLine($"Exception reading config file [{configKey}] from S3: {e}");
            } catch (Exception e) {
                Console.WriteLine($"Exception reading config file: {e}");
            }

        }

        private async Task<string> GetS3FileContent(string bucket, string key) {
            string responseBody = "";

            try {
                GetObjectRequest request = new GetObjectRequest {
                    BucketName = bucket,
                    Key = key
                };
                using (GetObjectResponse response = await S3Client.GetObjectAsync(request))
                using (Stream responseStream = response.ResponseStream)
                using (StreamReader reader = new StreamReader(responseStream)) {
                    responseBody = reader.ReadToEnd();
                }
            } catch (AmazonS3Exception e) {
                Console.WriteLine("S3 Exception encountered when writing object: {0}", e.Message);
            } catch (Exception e) {
                Console.WriteLine("Unknown Exception encountered when writing an object: {0}", e.Message);
            }
            return responseBody;
        }
    }

}
