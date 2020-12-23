using System;

namespace LucrumLabs
{
    public static class MathUtils
    {
        public static decimal InvLerp(decimal a, decimal b, decimal v)
        {
            return (v - a) / (b - a);
        }
        
        public static decimal GetRetracementPrice(decimal start, decimal end, decimal fibValue)
        {
            decimal result = 0m;

            var dt = Math.Abs(start - end) * fibValue;
            if (end >= start)
            {
                result = end - dt;
            }
            else
            {
                result = end + dt;
            }
            
            //Log(string.Format("Fib {0} - start: {1}, end: {2} = {3}", fibValue, start, end, result));
            
            return result;
        }
    }
}