using System;
using System.Collections.Generic;
using System.Text;

namespace UXSkill {
    public class SegmentItem {
        public decimal start_time { get; set; }
        public string speaker_label { get; set; }
        public decimal end_time { get; set; }
    }
    public class Segment {
        public decimal start_time { get; set; }
        public string speaker_label { get; set; }
        public decimal end_time { get; set; }
        public SegmentItem[] items { get; set; }
    }
}
