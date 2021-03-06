﻿using System;

namespace UniNativeLinq.Average
{
    public struct Int64Average : IAverageOperator<Int64, double>
    {
        private double count;
        public void Execute(ref double arg0, ref Int64 arg1)
        {
            arg0 += arg1;
            ++count;
        }

        public bool TryCalculateResult(double accumulate, out double result)
        {
            if (count == default)
            {
                result = default;
                return false;
            }
            result = accumulate / count;
            return true;
        }
    }
}