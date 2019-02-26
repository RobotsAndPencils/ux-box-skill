using System;
using System.Collections.Generic;

namespace UXSkill {
    public class Conversation {
        public decimal duration = 0;
        public Dictionary<int, List<SpeakerResult>> resultBySpeaker;
        public List<SpeakerResult> resultByTime;
        public Dictionary<int, Dictionary<string, List<SpeakerResult>>> resultsBySpeakerSentiment;
        public List<String> speakerLabels;
        public Dictionary<string, List<SpeakerResult>> topicLocations;
        public List<string> topics;

        public Conversation() {
            resultBySpeaker = new Dictionary<int, List<SpeakerResult>>();
            resultByTime = new List<SpeakerResult>();
            resultsBySpeakerSentiment = new Dictionary<int, Dictionary<string, List<SpeakerResult>>>();
            speakerLabels = new List<string>();
            topicLocations = new Dictionary<string, List<SpeakerResult>>();
        }
    }
}
