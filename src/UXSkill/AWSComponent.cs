using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Comprehend.Model;
using Amazon.TranscribeService.Model;
using Newtonsoft.Json.Linq;
using Amazon.S3.Model;
using Amazon.S3;

namespace UXSkill {
    public class AWSComponent {
        public struct JobStatus {
            public const string IN_PROGRESS = "IN_PROGRESS";
            public const string COMPLETED = "COMPLETED";
            public const string FAILED = "FAILED";
        }
        private static readonly Configuration config = Configuration.GetInstance.Result;
        private static Regex blankPattern = new Regex("^\\W*$");

        public AWSComponent() {
        }

        private static async Task<TranscriptionJob> WaitForCompletion(string jobName) {
            string status = JobStatus.IN_PROGRESS;
            TranscriptionJob foundJob = null;

            while (JobStatus.IN_PROGRESS.Equals(status)) {
                Thread.Sleep(10000);    // transcription takes a while, don't need to check back quickly
                foundJob = await GetTranscriptionJob(jobName);
                if (foundJob == null) {
                    Console.WriteLine($"{jobName} not found");
                    break;
                }
                status = foundJob?.TranscriptionJobStatus.Value;
            }
            Console.WriteLine($"Transcription job Finished");
            return foundJob;
        }

        private static async Task<TranscriptionJob> GetTranscriptionJob(string jobName) {
            TranscriptionJob job = null;
            try {
                var getTranscribeResponse = await config.AzTranscribeClient.GetTranscriptionJobAsync(new GetTranscriptionJobRequest() {
                    TranscriptionJobName = jobName
                });
                job = getTranscribeResponse?.TranscriptionJob;
            } catch (BadRequestException) { } //Do nothing, job not found
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
            return job;
        }

        public static async Task<Conversation> TranscribeMedia(string jobName, string fileName, string fileExt) {
            // Check for an existing job (maybe lambda was timed out and then re-run)
            var job = await GetTranscriptionJob(jobName);

            if (job == null || job?.TranscriptionJobStatus == null) {
                job = await StartTranscriptionJob(jobName, GetS3FileUrl(config.S3BucketName, fileName), fileExt);
            }

            switch (job?.TranscriptionJobStatus.Value) {
                case JobStatus.IN_PROGRESS:
                    job = await WaitForCompletion(jobName);
                    break;
                case JobStatus.FAILED:
                    Console.WriteLine("AWS Transcription job failed. Aborting");
                    return null;
            }
            var result = new Conversation();

            if (job?.TranscriptionJobStatus.Value == JobStatus.FAILED) {
                Console.WriteLine($"Transcription job failed with reason:  {job.FailureReason}");
            } else if (job.TranscriptionJobStatus.Value == JobStatus.COMPLETED) {
                Console.WriteLine($"Transcription file located @: {job.Transcript.TranscriptFileUri}");
                string results;
                using (var webClient = new WebClient()) {
                    results = webClient.DownloadString(job.Transcript.TranscriptFileUri);
                }

                JObject jsonResults = JObject.Parse(results);
                result = await ProcessTranscriptionResults(jsonResults);
            }
            return result;
        }

        private static async Task<TranscriptionJob> StartTranscriptionJob(string jobName, string mediaFileUri, string fileExt) {
            var transcriptionRequest = new StartTranscriptionJobRequest() {
                LanguageCode = config.Language,
                Media = new Media() { MediaFileUri = mediaFileUri },
                MediaFormat = fileExt,
                TranscriptionJobName = jobName,
                Settings = new Settings() {
                    MaxSpeakerLabels = config.MaxSpeakerLabels,
                    ShowSpeakerLabels = true
                },
            };
            Console.WriteLine($"Start Transcription job: {DateTime.Now}");
            var getTranscribeResponse = await config.AzTranscribeClient.StartTranscriptionJobAsync(transcriptionRequest);
            return getTranscribeResponse?.TranscriptionJob;
        }

        public static string GetS3FileUrl(string bucket, string key) {
            return $"https://s3.amazonaws.com/{bucket}/{key}";
        }

        private static async Task<Conversation> ProcessTranscriptionResults(JObject transcriptionResults) {
            var result = new Conversation();

            StringBuilder speakerText = new StringBuilder();
            TranscribeAlternative alternative = null;

            var segments = transcriptionResults["results"]["speaker_labels"]["segments"].ToObject<List<Segment>>();
            var transciptionsItems = transcriptionResults["results"]["items"].ToObject<List<TranscribeItem>>();

            Console.WriteLine($"items: {transciptionsItems?.Count} segments: {segments.Count}");

            var speakerLabel = string.Empty;
            var lastSpeaker = "nobody";
            SpeakerResult currentSpeakerResult = new SpeakerResult();

            var itemIdx = 0;

            var ti = transciptionsItems;
            // sements have a begin and end, however the items contained in it also
            // have begin and ends. the range of the items have a 1 to 1 correlation to the 'pronunciation' transcription
            // item types. These also have ends which are outside the range of the segement strangely. So will be using segment to
            // get the speaker, then will create an inclusive range for all items under it using the being of first and end of last. 
            foreach (var segment in segments) {
                if (segment.items.Length == 0) continue;

                result.duration = segment.end_time;

                if (!lastSpeaker.Equals(segment.speaker_label)) {
                    // these lines do nothing the first iteration, but tie up last
                    // speaker result when the speaker is changing
                    currentSpeakerResult.text = speakerText.ToString();
                    speakerText = new StringBuilder();

                    // create new speaker result for new speaker - or first speaker on first iteration 
                    var idx = result.speakerLabels.IndexOf(segment.speaker_label);
                    if (idx == -1) {
                        idx = result.speakerLabels.Count;
                        result.speakerLabels.Add(segment.speaker_label);
                        result.resultBySpeaker.Add(idx, new List<SpeakerResult>());
                    }

                    currentSpeakerResult = new SpeakerResult();
                    currentSpeakerResult.speaker = idx;
                    ConfigureTimeRange(ref currentSpeakerResult, segment);
                    lastSpeaker = segment.speaker_label;

                    result.resultBySpeaker[idx].Add(currentSpeakerResult);
                    result.resultByTime.Add(currentSpeakerResult);

                } else {
                    ConfigureTimeRange(ref currentSpeakerResult, segment);
                }

                for (; itemIdx < ti.Count
                     && ((currentSpeakerResult.start <= ti[itemIdx].start_time && ti[itemIdx].end_time <= currentSpeakerResult.end)
                         || (ti[itemIdx].start_time == 0m))
                     ; itemIdx++) {
                    alternative = ti[itemIdx].alternatives.First();
                    if (alternative.content.Equals("[SILENCE]")) {
                        speakerText.Append(".");
                    } else {
                        speakerText.Append(alternative.content);
                    }
                    speakerText.Append(" ");
                }

            }
            currentSpeakerResult.text = speakerText.ToString();

            // Call AWS Comprehend client to get sentiment for all speaker results
            List<int> keyList = new List<int>(result.resultBySpeaker.Keys);
            for (int keyIdx = 0; keyIdx < keyList.Count; keyIdx++) {
                var spkKey = keyList[keyIdx];
                for (int resultIdx = result.resultBySpeaker[spkKey].Count - 1; resultIdx >= 0; resultIdx--) {
                    if (!IsBlankText(result.resultBySpeaker[spkKey][resultIdx].text)) {
                        var speakerResult = result.resultBySpeaker[spkKey][resultIdx];
                        speakerResult.sentiment = await DetermineSentiment(result.resultBySpeaker[spkKey][resultIdx].text);
                        var topics = await DetermineTopic(result.resultBySpeaker[spkKey][resultIdx].text);
                        foreach (var topic in topics) {
                            if (!result.topicLocations.ContainsKey(topic.Text)) {
                                result.topicLocations.Add(topic.Text, new List<SpeakerResult>());
                            }
                            result.topicLocations[topic.Text].Add(speakerResult);
                        }
                    }
                }
            }

            return result;
        }
        private static bool IsBlankText(string text) {
            return blankPattern.Match(text).Success;
        }

        private static void ConfigureTimeRange(ref SpeakerResult currentSpeakerResult, Segment segment) {
            foreach (var item in segment.items) {
                if (currentSpeakerResult.end == 0m) currentSpeakerResult.start = item.start_time;
                currentSpeakerResult.end = item.end_time;
            }
        }
        public static async Task<DetectSentimentResponse> DetermineSentiment(string text) {
            // Call DetectKeyPhrases API        
            DetectSentimentRequest detectSentimentRequest = new DetectSentimentRequest() {
                Text = text,
                LanguageCode = "en"
            };
            DetectSentimentResponse detectSentimentResponse = await config.AzComprehendClient.DetectSentimentAsync(detectSentimentRequest);

            return detectSentimentResponse;
        }
        public static async Task<List<KeyPhrase>> DetermineTopic(string text) {
            DetectKeyPhrasesRequest request = new DetectKeyPhrasesRequest() {
                LanguageCode = "en",
                Text = text
            };
            DetectKeyPhrasesResponse response = await config.AzComprehendClient.DetectKeyPhrasesAsync(request);
            var result = new List<KeyPhrase>();
            // Filter using a score of .9 as the threshold
            if (response != null && response.KeyPhrases.Count > 0) {
                foreach (var phrase in response.KeyPhrases) {
                    if (phrase.Score > -.7 && phrase.Text.Length > 4) {
                        Console.WriteLine($"=== Phrase [{phrase.Text}] score: {phrase.Score}");
                        var str = phrase.Text;
                        phrase.Text = str.Remove(1).ToUpper() + str.Remove(0, 1).ToLower();
                        result.Add(phrase);
                    }
                }
            }
            return result;
        }
        public static async Task DeleteObjectNonVersionedBucketAsync(string key) {
            try {
                var deleteObjectRequest = new DeleteObjectRequest {
                    BucketName = config.S3BucketName,
                    Key = key
                };

                Console.WriteLine("Deleting an object");
                await config.S3Client.DeleteObjectAsync(deleteObjectRequest);
            } catch (AmazonS3Exception e) {
                Console.WriteLine("Error encountered on server. Message:'{0}' when writing an object", e.Message);
            } catch (Exception e) {
                Console.WriteLine("Unknown encountered on server. Message:'{0}' when writing an object", e.Message);
            }
        }
    }
}
