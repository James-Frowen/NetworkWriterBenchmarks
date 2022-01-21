using System.Collections;
using JamesFrowen.Benchmarker;
using JamesFrowen.Benchmarker.Weaver;
using Mirror;
using Unity.Netcode;
using UnityEngine;

namespace RunBenchmark
{
    public class Runner : MonoBehaviour
    {
        public IEnumerator Start()
        {
            BenchmarkRunner.ResultFolder = "./Results/";

            BenchmarkHelper.StartRecording(300, false, false);

            var mirage = new MirageWriter();
            yield return mirage.Start();
            yield return null;

            var mirageCopy = new MirageWriter_Copy();
            yield return mirageCopy.Start();
            yield return null;

            var mirror = new MirrorWriter();
            yield return mirror.Start();
            yield return null;

            var unity = new UnityWriter_FastBufferWriter();
            yield return unity.Start();
            yield return null;

            BenchmarkHelper.EndRecording();
            BenchmarkRunner.LogResults();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>
    /// Inherit from this to set up and run benchmarks
    /// </summary>
    public abstract class Benchmark
    {
        public int WarmupCount = 30;
        public int RunCount = 300;
        public int Iterations = 1000;

        protected virtual void Setup() { }
        protected virtual void Teardown() { }
        protected abstract void Code();

        public IEnumerator Start()
        {
            Setup();
            yield return null;

            Warmup();
            Measure();

            yield return null;

            Teardown();
        }

        private void Warmup()
        {
            BenchmarkHelper.PauseRecording(true);

            for (int i = 0; i < WarmupCount; i++)
            {
                for (int j = 0; j < Iterations; j++)
                {
                    Code();
                }
            }

            BenchmarkHelper.PauseRecording(false);
        }
        private void Measure()
        {
            for (int i = 0; i < RunCount; i++)
            {
                for (int j = 0; j < Iterations; j++)
                {
                    Code();
                }

                BenchmarkHelper.NextFrame();
            }
        }
    }


    public class MirageWriter : Benchmark
    {
        JamesFrowen.BitPacking.NetworkWriter writer;

        protected override void Setup()
        {
            writer = new JamesFrowen.BitPacking.NetworkWriter(1500);
        }
        protected override void Teardown()
        {
            writer.Reset();
            writer = null;
        }

        [BenchmarkMethod(name: "Mirage")]
        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                writer.Reset();

                writer.WriteByte((byte)i);
                writer.WriteInt32(i);
                writer.WriteInt32(i);
                writer.WriteUInt64((ulong)i);
                writer.WriteByte((byte)i);
                writer.WriteByte((byte)i);
                writer.WriteByte((byte)i);
                writer.WriteByte((byte)i);
            }
        }
    }
    public class MirageWriter_Copy : Benchmark
    {
        JamesFrowen.BitPacking.NetworkWriter writer;

        protected override void Setup()
        {
            writer = new JamesFrowen.BitPacking.NetworkWriter(1500);
        }
        protected override void Teardown()
        {
            writer.Reset();
            writer = null;
        }

        [BenchmarkMethod(name: "Mirage Copy")]
        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                writer.Reset();

                byte b1 = (byte)i;
                writer.PadAndCopy(b1);
                writer.PadAndCopy(i);
                writer.PadAndCopy(i);
                ulong u1 = (ulong)i;
                writer.PadAndCopy(u1);
                byte b2 = (byte)i;
                byte b3 = (byte)i;
                byte b4 = (byte)i;
                byte b5 = (byte)i;
                writer.PadAndCopy(b2);
                writer.PadAndCopy(b3);
                writer.PadAndCopy(b4);
                writer.PadAndCopy(b5);
            }
        }
    }
    public class MirrorWriter : Benchmark
    {
        Mirror.NetworkWriter writer;
        protected override void Setup()
        {
            writer = new Mirror.NetworkWriter();
        }
        protected override void Teardown()
        {
            writer.Reset();
            writer = null;
        }

        [BenchmarkMethod(name: "Mirror")]
        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                writer.Reset();

                writer.WriteByte((byte)i);
                writer.WriteInt(i);
                writer.WriteInt(i);
                writer.WriteULong((ulong)i);
                writer.WriteByte((byte)i);
                writer.WriteByte((byte)i);
                writer.WriteByte((byte)i);
                writer.WriteByte((byte)i);
            }
        }
    }
    /*
    public class UnityWriter_BitWriter : Benchmark
    {
        // todo how do we use this??
        Unity.Netcode.BitWriter writer;
        protected override void Setup()
        {
            writer = new Unity.Netcode.BitWriter();
        }
        protected override void Teardown()
        {
            base.Teardown();
        }

        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                this.writer.Reset();

                this.writer.WriteByte((byte)i);
                this.writer.WriteInt(i);
                this.writer.WriteInt(i);
                this.writer.WriteULong((ulong)i);
                this.writer.WriteByte((byte)i);
                this.writer.WriteByte((byte)i);
                this.writer.WriteByte((byte)i);
                this.writer.WriteByte((byte)i);
            }
        }
    }
    */
    public class UnityWriter_FastBufferWriter : Benchmark
    {
        private FastBufferWriter writer;

        protected override void Setup()
        {
            // todo can you not reset writer? do you have to create new one each time
            writer = new Unity.Netcode.FastBufferWriter(1500, Unity.Collections.Allocator.Temp);
        }
        protected override void Teardown()
        {
            writer.Dispose();
        }

        [BenchmarkMethod(name: "Unity")]
        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                writer.Seek(0);

                writer.TryBeginWrite(21);
                writer.WriteValue((byte)i);
                writer.WriteValue(i);
                writer.WriteValue(i);
                writer.WriteValue((ulong)i);
                writer.WriteValue((byte)i);
                writer.WriteValue((byte)i);
                writer.WriteValue((byte)i);
                writer.WriteValue((byte)i);
            }
        }
    }
}
