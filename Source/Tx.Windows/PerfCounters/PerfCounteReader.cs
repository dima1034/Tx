﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tx.Windows
{
    public abstract class PerfCounterReader : IDisposable
    {
        internal readonly List<PerfCounterInfo> _counters = new List<PerfCounterInfo>();
        internal readonly IObserver<PerformanceSample> _observer;
        internal PdhQueryHandle _query;

        public PerfCounterReader(IObserver<PerformanceSample> observer)
        {
            _observer = observer;
        }

        internal void ProduceCounterSamples(PerfCounterInfo counterInfo, DateTime timestamp)
        {
            uint bufferSize = 0;
            uint bufferCount;

            PdhStatus status = PdhNativeMethods.PdhGetFormattedCounterArray(
                counterInfo.Handle,
                PdhFormat.PDH_FMT_DOUBLE,
                ref bufferSize,
                out bufferCount,
                IntPtr.Zero);
            PdhUtils.CheckStatus(status, PdhStatus.PDH_MORE_DATA);

            var buffer = new byte[bufferSize];
            unsafe
            {
                fixed (byte* pb = buffer)
                {
                    status = PdhNativeMethods.PdhGetFormattedCounterArray(
                        counterInfo.Handle,
                        PdhFormat.PDH_FMT_DOUBLE,
                        ref bufferSize,
                        out bufferCount,
                        (IntPtr) pb);
                    if (status == PdhStatus.PDH_INVALID_DATA
                        || status == PdhStatus.PDH_CALC_NEGATIVE_VALUE
                        || status == PdhStatus.PDH_CALC_NEGATIVE_DENOMINATOR
                        || status == PdhStatus.PDH_CALC_NEGATIVE_TIMEBASE)
                    {
                        var sample = new PerformanceSample(counterInfo, counterInfo.Instance, timestamp, double.NaN);
                        _observer.OnNext(sample);
                        return;
                    }

                    PdhUtils.CheckStatus(status, PdhStatus.PDH_CSTATUS_VALID_DATA);

                    var items = (PDH_FMT_COUNTERVALUE_ITEM*) pb;
                    for (int i = 0; i < bufferCount; i++)
                    {
                        PDH_FMT_COUNTERVALUE_ITEM* item = items + i;
                        var instanceName = new string((char*)item->szName);
                        var sample = new PerformanceSample(counterInfo, instanceName, timestamp, item->FmtValue.doubleValue);

                        _observer.OnNext(sample);
                    }
                }
            }
        }

        protected void AddCounter(string counterPath)
        {
            PdhCounterHandle counter;
            PdhStatus status = PdhNativeMethods.PdhAddCounter(_query, counterPath, IntPtr.Zero, out counter);
            if (status == PdhStatus.PDH_ENTRY_NOT_IN_LOG_FILE)
                return;

            PdhUtils.CheckStatus(status, PdhStatus.PDH_CSTATUS_VALID_DATA);

            var counterInfo = new PerfCounterInfo(counterPath, counter);
            _counters.Add(counterInfo);
        }

        public virtual void Dispose()
        {
            foreach (PerfCounterInfo counterInfo in _counters)
            {
                counterInfo.Dispose();
            }

            _query.Dispose();
        }
    }
}