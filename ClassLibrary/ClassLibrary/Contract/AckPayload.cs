using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassLibrary.Contract
{
    public class AckPayload
    {
        public string producerId { get; set; }
        public string consumerId { get; set; }
        public int sequenceId { get; set; }
    }
}
