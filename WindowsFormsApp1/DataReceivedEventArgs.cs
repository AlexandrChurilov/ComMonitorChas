using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public class DataReceivedEventArgs: EventArgs
    {
        public byte Byte1 {  get; }
        public byte Byte2 {  get; }
        public DateTime Timestamp { get; }
        public DataReceivedEventArgs(DateTime dateTime , byte byte1, byte byte2) 
        {
            Timestamp= dateTime;
            Byte1 = byte1;
            Byte2 = byte2;
        }
    }
}
