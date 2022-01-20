using JamesFrowen.Benchmarker;
using JamesFrowen.Benchmarker.Weaver;
using Mirror;
using System.Collections;
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
            this.Setup();
            yield return null;

            this.Warmup();
            this.Measure();

            yield return null;

            this.Teardown();
        }

        private void Warmup()
        {
            for (int i = 0; i < this.WarmupCount; i++)
            {
                for (int j = 0; j < this.Iterations; j++)
                {
                    this.Code();
                }
            }
        }
        private void Measure()
        {
            for (int i = 0; i < this.RunCount; i++)
            {
                for (int j = 0; j < this.Iterations; j++)
                {
                    this.Code();
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
            this.writer = new JamesFrowen.BitPacking.NetworkWriter(1500);
        }
        protected override void Teardown()
        {
            this.writer.Reset();
            this.writer = null;
        }

        [BenchmarkMethod(name: "Mirage")]
        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                this.writer.Reset();

                this.writer.WriteByte((byte)i);
                this.writer.WriteInt32(i);
                this.writer.WriteInt32(i);
                this.writer.WriteUInt64((ulong)i);
                this.writer.WriteByte((byte)i);
                this.writer.WriteByte((byte)i);
                this.writer.WriteByte((byte)i);
                this.writer.WriteByte((byte)i);
            }
        }
    }
    public class MirrorWriter : Benchmark
    {
        Mirror.NetworkWriter writer;
        protected override void Setup()
        {
            this.writer = new Mirror.NetworkWriter();
        }
        protected override void Teardown()
        {
            this.writer.Reset();
            this.writer = null;
        }

        [BenchmarkMethod(name: "Mirror")]
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
            this.writer = new Unity.Netcode.FastBufferWriter(1500, Unity.Collections.Allocator.Temp);
        }
        protected override void Teardown()
        {
            this.writer.Dispose();
        }

        [BenchmarkMethod(name: "Unity")]
        protected override void Code()
        {
            for (int i = 0; i < 100; i++)
            {
                this.writer.Seek(0);

                this.writer.TryBeginWrite(21);
                this.writer.WriteValue((byte)i);
                this.writer.WriteValue(i);
                this.writer.WriteValue(i);
                this.writer.WriteValue((ulong)i);
                this.writer.WriteValue((byte)i);
                this.writer.WriteValue((byte)i);
                this.writer.WriteValue((byte)i);
                this.writer.WriteValue((byte)i);
            }
        }
    }
}
