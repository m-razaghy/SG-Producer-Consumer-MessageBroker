namespace ClassLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class RateLimitAttribute : Attribute
    {
        public int RateLimit { get; }

        public RateLimitAttribute(int rateLimit)
        {
            RateLimit = rateLimit;
        }
    }
}
