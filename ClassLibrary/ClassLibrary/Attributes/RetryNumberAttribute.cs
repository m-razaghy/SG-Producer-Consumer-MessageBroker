using System;
using System.Collections.Generic;
using System.Linq;
namespace ClassLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class RetryNumberAttribute : Attribute
    {
        public int RetryNumber { get; }

        public RetryNumberAttribute(int retryNumber)
        {
            RetryNumber = retryNumber;
        }
    }
}
