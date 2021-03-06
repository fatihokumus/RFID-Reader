﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RFReaderConsole.DTO
{
    public class TagMasterDto
    {
        public string tags { get; set; }
    }
    public class TagDto
    {
        public TagFieldDto fields { get; set; }
    }
    public class TagFieldDto
    {
        public string Code { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public int BestRSSI { get; set; }
        public List<ReadingInfo> ReadingInfo { get; set; }
    }
    public class ReadingInfo
    {
        public int RSSI { get; set; }
        public DateTime ReadingTime { get; set; }
    }
}
