using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1
{
    public struct DataPoint
    {
        public DateTime Timestamp;
        public byte Byte1;
        public byte Byte2;

        public DataPoint(DateTime timestamp, byte byte1, byte byte2)
        {
            Timestamp = timestamp;
            Byte1 = byte1;
            Byte2 = byte2;
        }
    }
}
