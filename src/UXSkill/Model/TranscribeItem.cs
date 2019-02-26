using System;
using System.Collections.Generic;
using System.Text;

namespace UXSkill {
    public class TranscribeItem {
        public decimal start_time { get; set; }
        public string type { get; set; }
        public decimal end_time { get; set; }
        public List<TranscribeAlternative> alternatives { get; set; }
    }

    public class TranscribeAlternative {
        private double _confidence = 0;
        public double? confidence {
            get {
                return _confidence;
            }
            set {
                if (value == null) {
                    _confidence = 0;
                } else {
                    _confidence = (double)value;
                }
            }
        }
        public string content { get; set; }
    }
}
