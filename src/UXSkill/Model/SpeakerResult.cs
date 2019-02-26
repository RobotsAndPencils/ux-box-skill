using Amazon.Comprehend.Model;

// Used for results after processing response from AWS transcribe and comprehend
namespace UXSkill {
    public class SpeakerResult {
        public decimal start;
        public decimal end;
        public string text;
        public int speaker;
        public DetectSentimentResponse sentiment;
    }
}