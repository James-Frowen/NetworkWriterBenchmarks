using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Netcode
{
    public struct FastBufferWriter : IDisposable
    {
        internal struct WriterHandle
        {
            internal unsafe byte* BufferPointer;
            internal int Position;
            internal int Length;
            internal int Capacity;
            internal int MaxCapacity;
            internal Allocator Allocator;
            internal bool BufferGrew;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            internal int AllowedWriteMark;
            internal bool InBitwiseContext;
#endif
        }

        internal readonly unsafe WriterHandle* Handle;

        private static byte[] s_ByteArrayCache = new byte[65535];

        /// <summary>
        /// The current write position
        /// </summary>
        public unsafe int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.Handle->Position;
        }

        /// <summary>
        /// The current total buffer size
        /// </summary>
        public unsafe int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.Handle->Capacity;
        }

        /// <summary>
        /// The maximum possible total buffer size
        /// </summary>
        public unsafe int MaxCapacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.Handle->MaxCapacity;
        }

        /// <summary>
        /// The total amount of bytes that have been written to the stream
        /// </summary>
        public unsafe int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.Handle->Position > this.Handle->Length ? this.Handle->Position : this.Handle->Length;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe void CommitBitwiseWrites(int amount)
        {
            this.Handle->Position += amount;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.Handle->InBitwiseContext = false;
#endif
        }

        /// <summary>
        /// Create a FastBufferWriter.
        /// </summary>
        /// <param name="size">Size of the buffer to create</param>
        /// <param name="allocator">Allocator to use in creating it</param>
        /// <param name="maxSize">Maximum size the buffer can grow to. If less than size, buffer cannot grow.</param>
        public unsafe FastBufferWriter(int size, Allocator allocator, int maxSize = -1)
        {
            // Allocating both the Handle struct and the buffer in a single allocation - sizeof(WriterHandle) + size
            // The buffer for the initial allocation is the next block of memory after the handle itself.
            // If the buffer grows, a new buffer will be allocated and the handle pointer pointed at the new location...
            // The original buffer won't be deallocated until the writer is destroyed since it's part of the handle allocation.
            this.Handle = (WriterHandle*)UnsafeUtility.Malloc(sizeof(WriterHandle) + size, UnsafeUtility.AlignOf<WriterHandle>(), allocator);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            UnsafeUtility.MemSet(this.Handle, 0, sizeof(WriterHandle) + size);
#endif
            this.Handle->BufferPointer = (byte*)(this.Handle + 1);
            this.Handle->Position = 0;
            this.Handle->Length = 0;
            this.Handle->Capacity = size;
            this.Handle->Allocator = allocator;
            this.Handle->MaxCapacity = maxSize < size ? size : maxSize;
            this.Handle->BufferGrew = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.Handle->AllowedWriteMark = 0;
            this.Handle->InBitwiseContext = false;
#endif
        }

        /// <summary>
        /// Frees the allocated buffer
        /// </summary>
        public unsafe void Dispose()
        {
            if (this.Handle->BufferGrew)
            {
                UnsafeUtility.Free(this.Handle->BufferPointer, this.Handle->Allocator);
            }
            UnsafeUtility.Free(this.Handle, this.Handle->Allocator);
        }

        /// <summary>
        /// Move the write position in the stream.
        /// Note that moving forward past the current length will extend the buffer's Length value even if you don't write.
        /// </summary>
        /// <param name="where">Absolute value to move the position to, truncated to Capacity</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Seek(int where)
        {
            // This avoids us having to synchronize length all the time.
            // Writing things is a much more common operation than seeking
            // or querying length. The length here is a high watermark of
            // what's been written. So before we seek, if the current position
            // is greater than the length, we update that watermark.
            // When querying length later, we'll return whichever of the two
            // values is greater, thus if we write past length, length increases
            // because position increases, and if we seek backward, length remembers
            // the position it was in.
            // Seeking forward will not update the length.
            where = Math.Min(where, this.Handle->Capacity);
            if (this.Handle->Position > this.Handle->Length && where < this.Handle->Position)
            {
                this.Handle->Length = this.Handle->Position;
            }

            this.Handle->Position = where;
        }

        /// <summary>
        /// Truncate the stream by setting Length to the specified value.
        /// If Position is greater than the specified value, it will be moved as well.
        /// </summary>
        /// <param name="where">The value to truncate to. If -1, the current position will be used.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Truncate(int where = -1)
        {
            if (where == -1)
            {
                where = this.Position;
            }

            if (this.Handle->Position > where)
            {
                this.Handle->Position = where;
            }

            if (this.Handle->Length > where)
            {
                this.Handle->Length = where;
            }
        }

        /// <summary>
        /// Retrieve a BitWriter to be able to perform bitwise operations on the buffer.
        /// No bytewise operations can be performed on the buffer until bitWriter.Dispose() has been called.
        /// At the end of the operation, FastBufferWriter will remain byte-aligned.
        /// </summary>
        /// <returns>A BitWriter</returns>
        public unsafe BitWriter EnterBitwiseContext()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.Handle->InBitwiseContext = true;
#endif
            return new BitWriter(this);
        }

        internal unsafe void Grow(int additionalSizeRequired)
        {
            int desiredSize = this.Handle->Capacity * 2;
            while (desiredSize < this.Position + additionalSizeRequired)
            {
                desiredSize *= 2;
            }

            int newSize = Math.Min(desiredSize, this.Handle->MaxCapacity);
            byte* newBuffer = (byte*)UnsafeUtility.Malloc(newSize, UnsafeUtility.AlignOf<byte>(), this.Handle->Allocator);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            UnsafeUtility.MemSet(newBuffer, 0, newSize);
#endif
            UnsafeUtility.MemCpy(newBuffer, this.Handle->BufferPointer, this.Length);
            if (this.Handle->BufferGrew)
            {
                UnsafeUtility.Free(this.Handle->BufferPointer, this.Handle->Allocator);
            }

            this.Handle->BufferGrew = true;
            this.Handle->BufferPointer = newBuffer;
            this.Handle->Capacity = newSize;
        }

        /// <summary>
        /// Allows faster serialization by batching bounds checking.
        /// When you know you will be writing multiple fields back-to-back and you know the total size,
        /// you can call TryBeginWrite() once on the total size, and then follow it with calls to
        /// WriteValue() instead of WriteValueSafe() for faster serialization.
        /// 
        /// Unsafe write operations will throw OverflowException in editor and development builds if you
        /// go past the point you've marked using TryBeginWrite(). In release builds, OverflowException will not be thrown
        /// for performance reasons, since the point of using TryBeginWrite is to avoid bounds checking in the following
        /// operations in release builds.
        /// </summary>
        /// <param name="bytes">Amount of bytes to write</param>
        /// <returns>True if the write is allowed, false otherwise</returns>
        /// <exception cref="InvalidOperationException">If called while in a bitwise context</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryBeginWrite(int bytes)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif
            if (this.Handle->Position + bytes > this.Handle->Capacity)
            {
                if (this.Handle->Position + bytes > this.Handle->MaxCapacity)
                {
                    return false;
                }

                if (this.Handle->Capacity < this.Handle->MaxCapacity)
                {
                    this.Grow(bytes);
                }
                else
                {
                    return false;
                }
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.Handle->AllowedWriteMark = this.Handle->Position + bytes;
#endif
            return true;
        }

        /// <summary>
        /// Allows faster serialization by batching bounds checking.
        /// When you know you will be writing multiple fields back-to-back and you know the total size,
        /// you can call TryBeginWrite() once on the total size, and then follow it with calls to
        /// WriteValue() instead of WriteValueSafe() for faster serialization.
        /// 
        /// Unsafe write operations will throw OverflowException in editor and development builds if you
        /// go past the point you've marked using TryBeginWrite(). In release builds, OverflowException will not be thrown
        /// for performance reasons, since the point of using TryBeginWrite is to avoid bounds checking in the following
        /// operations in release builds. Instead, attempting to write past the marked position in release builds
        /// will write to random memory and cause undefined behavior, likely including instability and crashes.
        /// </summary>
        /// <param name="value">The value you want to write</param>
        /// <returns>True if the write is allowed, false otherwise</returns>
        /// <exception cref="InvalidOperationException">If called while in a bitwise context</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryBeginWriteValue<T>(in T value) where T : unmanaged
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif
            int len = sizeof(T);
            if (this.Handle->Position + len > this.Handle->Capacity)
            {
                if (this.Handle->Position + len > this.Handle->MaxCapacity)
                {
                    return false;
                }

                if (this.Handle->Capacity < this.Handle->MaxCapacity)
                {
                    this.Grow(len);
                }
                else
                {
                    return false;
                }
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.Handle->AllowedWriteMark = this.Handle->Position + len;
#endif
            return true;
        }

        /// <summary>
        /// Internal version of TryBeginWrite.
        /// Differs from TryBeginWrite only in that it won't ever move the AllowedWriteMark backward.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryBeginWriteInternal(int bytes)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif
            if (this.Handle->Position + bytes > this.Handle->Capacity)
            {
                if (this.Handle->Position + bytes > this.Handle->MaxCapacity)
                {
                    return false;
                }

                if (this.Handle->Capacity < this.Handle->MaxCapacity)
                {
                    this.Grow(bytes);
                }
                else
                {
                    return false;
                }
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->Position + bytes > this.Handle->AllowedWriteMark)
            {
                this.Handle->AllowedWriteMark = this.Handle->Position + bytes;
            }
#endif
            return true;
        }

        /// <summary>
        /// Returns an array representation of the underlying byte buffer.
        /// !!Allocates a new array!!
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte[] ToArray()
        {
            byte[] ret = new byte[this.Length];
            fixed (byte* b = ret)
            {
                UnsafeUtility.MemCpy(b, this.Handle->BufferPointer, this.Length);
            }

            return ret;
        }

        /// <summary>
        /// Uses a static cached array to create an array segment with no allocations.
        /// This array can only be used until the next time ToTempByteArray() is called on ANY FastBufferWriter,
        /// as the cached buffer is shared by all of them and will be overwritten.
        /// As such, this should be used with care.
        /// </summary>
        /// <returns></returns>
        internal unsafe ArraySegment<byte> ToTempByteArray()
        {
            int length = this.Length;
            if (length > s_ByteArrayCache.Length)
            {
                return new ArraySegment<byte>(this.ToArray(), 0, length);
            }

            fixed (byte* b = s_ByteArrayCache)
            {
                UnsafeUtility.MemCpy(b, this.Handle->BufferPointer, length);
            }

            return new ArraySegment<byte>(s_ByteArrayCache, 0, length);
        }

        /// <summary>
        /// Gets a direct pointer to the underlying buffer
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* GetUnsafePtr()
        {
            return this.Handle->BufferPointer;
        }

        /// <summary>
        /// Gets a direct pointer to the underlying buffer at the current read position
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* GetUnsafePtrAtCurrentPosition()
        {
            return this.Handle->BufferPointer + this.Handle->Position;
        }

        /// <summary>
        /// Get the required size to write a string
        /// </summary>
        /// <param name="s">The string to write</param>
        /// <param name="oneByteChars">Whether or not to use one byte per character. This will only allow ASCII</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetWriteSize(string s, bool oneByteChars = false)
        {
            return sizeof(int) + s.Length * (oneByteChars ? sizeof(byte) : sizeof(char));
        }

        /// <summary>
        /// Write an INetworkSerializable
        /// </summary>
        /// <param name="value">The value to write</param>
        /// <typeparam name="T"></typeparam>
        public void WriteNetworkSerializable<T>(in T value) => throw new NotSupportedException();
        /*where T : INetworkSerializable
    {

        var bufferSerializer = new BufferSerializer<BufferSerializerWriter>(new BufferSerializerWriter(this));
        value.NetworkSerialize(bufferSerializer);
    }*/

        /// <summary>
        /// Write an array of INetworkSerializables
        /// </summary>
        /// <param name="array">The value to write</param>
        /// <param name="count"></param>
        /// <param name="offset"></param>
        /// <typeparam name="T"></typeparam>
        public void WriteNetworkSerializable<T>(T[] array, int count = -1, int offset = 0) => throw new NotSupportedException();
        /*where T : INetworkSerializable
        {
            int sizeInTs = count != -1 ? count : array.Length - offset;
            WriteValueSafe(sizeInTs);
            foreach (var item in array)
            {
                WriteNetworkSerializable(item);
            }
        }*/

        /// <summary>
        /// Writes a string
        /// </summary>
        /// <param name="s">The string to write</param>
        /// <param name="oneByteChars">Whether or not to use one byte per character. This will only allow ASCII</param>
        public unsafe void WriteValue(string s, bool oneByteChars = false)
        {
            this.WriteValue((uint)s.Length);
            int target = s.Length;
            if (oneByteChars)
            {
                for (int i = 0; i < target; ++i)
                {
                    this.WriteByte((byte)s[i]);
                }
            }
            else
            {
                fixed (char* native = s)
                {
                    this.WriteBytes((byte*)native, target * sizeof(char));
                }
            }
        }

        /// <summary>
        /// Writes a string
        ///
        /// "Safe" version - automatically performs bounds checking. Less efficient than bounds checking
        /// for multiple writes at once by calling TryBeginWrite.
        /// </summary>
        /// <param name="s">The string to write</param>
        /// <param name="oneByteChars">Whether or not to use one byte per character. This will only allow ASCII</param>
        public unsafe void WriteValueSafe(string s, bool oneByteChars = false)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif

            int sizeInBytes = GetWriteSize(s, oneByteChars);

            if (!this.TryBeginWriteInternal(sizeInBytes))
            {
                throw new OverflowException("Writing past the end of the buffer");
            }

            this.WriteValue((uint)s.Length);
            int target = s.Length;
            if (oneByteChars)
            {
                for (int i = 0; i < target; ++i)
                {
                    this.WriteByte((byte)s[i]);
                }
            }
            else
            {
                fixed (char* native = s)
                {
                    this.WriteBytes((byte*)native, target * sizeof(char));
                }
            }
        }

        /// <summary>
        /// Get the required size to write an unmanaged array
        /// </summary>
        /// <param name="array">The array to write</param>
        /// <param name="count">The amount of elements to write</param>
        /// <param name="offset">Where in the array to start</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetWriteSize<T>(T[] array, int count = -1, int offset = 0) where T : unmanaged
        {
            int sizeInTs = count != -1 ? count : array.Length - offset;
            int sizeInBytes = sizeInTs * sizeof(T);
            return sizeof(int) + sizeInBytes;
        }

        /// <summary>
        /// Writes an unmanaged array
        /// </summary>
        /// <param name="array">The array to write</param>
        /// <param name="count">The amount of elements to write</param>
        /// <param name="offset">Where in the array to start</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteValue<T>(T[] array, int count = -1, int offset = 0) where T : unmanaged
        {
            int sizeInTs = count != -1 ? count : array.Length - offset;
            int sizeInBytes = sizeInTs * sizeof(T);
            this.WriteValue(sizeInTs);
            fixed (T* native = array)
            {
                byte* bytes = (byte*)(native + offset);
                this.WriteBytes(bytes, sizeInBytes);
            }
        }

        /// <summary>
        /// Writes an unmanaged array
        ///
        /// "Safe" version - automatically performs bounds checking. Less efficient than bounds checking
        /// for multiple writes at once by calling TryBeginWrite.
        /// </summary>
        /// <param name="array">The array to write</param>
        /// <param name="count">The amount of elements to write</param>
        /// <param name="offset">Where in the array to start</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteValueSafe<T>(T[] array, int count = -1, int offset = 0) where T : unmanaged
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif

            int sizeInTs = count != -1 ? count : array.Length - offset;
            int sizeInBytes = sizeInTs * sizeof(T);

            if (!this.TryBeginWriteInternal(sizeInBytes + sizeof(int)))
            {
                throw new OverflowException("Writing past the end of the buffer");
            }
            this.WriteValue(sizeInTs);
            fixed (T* native = array)
            {
                byte* bytes = (byte*)(native + offset);
                this.WriteBytes(bytes, sizeInBytes);
            }
        }

        /// <summary>
        /// Write a partial value. The specified number of bytes is written from the value and the rest is ignored.
        /// </summary>
        /// <param name="value">Value to write</param>
        /// <param name="bytesToWrite">Number of bytes</param>
        /// <param name="offsetBytes">Offset into the value to begin reading the bytes</param>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="OverflowException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WritePartialValue<T>(T value, int bytesToWrite, int offsetBytes = 0) where T : unmanaged
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
            if (this.Handle->Position + bytesToWrite > this.Handle->AllowedWriteMark)
            {
                throw new OverflowException($"Attempted to write without first calling {nameof(TryBeginWrite)}()");
            }
#endif

            byte* ptr = ((byte*)&value) + offsetBytes;
            byte* bufferPointer = this.Handle->BufferPointer + this.Handle->Position;
            UnsafeUtility.MemCpy(bufferPointer, ptr, bytesToWrite);

            this.Handle->Position += bytesToWrite;
        }

        /// <summary>
        /// Write a byte to the stream.
        /// </summary>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteByte(byte value)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
            if (this.Handle->Position + 1 > this.Handle->AllowedWriteMark)
            {
                throw new OverflowException($"Attempted to write without first calling {nameof(TryBeginWrite)}()");
            }
#endif
            this.Handle->BufferPointer[this.Handle->Position++] = value;
        }

        /// <summary>
        /// Write a byte to the stream.
        ///
        /// "Safe" version - automatically performs bounds checking. Less efficient than bounds checking
        /// for multiple writes at once by calling TryBeginWrite.
        /// </summary>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteByteSafe(byte value)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif

            if (!this.TryBeginWriteInternal(1))
            {
                throw new OverflowException("Writing past the end of the buffer");
            }
            this.Handle->BufferPointer[this.Handle->Position++] = value;
        }

        /// <summary>
        /// Write multiple bytes to the stream
        /// </summary>
        /// <param name="value">Value to write</param>
        /// <param name="size">Number of bytes to write</param>
        /// <param name="offset">Offset into the buffer to begin writing</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteBytes(byte* value, int size, int offset = 0)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
            if (this.Handle->Position + size > this.Handle->AllowedWriteMark)
            {
                throw new OverflowException($"Attempted to write without first calling {nameof(TryBeginWrite)}()");
            }
#endif
            UnsafeUtility.MemCpy((this.Handle->BufferPointer + this.Handle->Position), value + offset, size);
            this.Handle->Position += size;
        }

        /// <summary>
        /// Write multiple bytes to the stream
        ///
        /// "Safe" version - automatically performs bounds checking. Less efficient than bounds checking
        /// for multiple writes at once by calling TryBeginWrite.
        /// </summary>
        /// <param name="value">Value to write</param>
        /// <param name="size">Number of bytes to write</param>
        /// <param name="offset">Offset into the buffer to begin writing</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteBytesSafe(byte* value, int size, int offset = 0)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif

            if (!this.TryBeginWriteInternal(size))
            {
                throw new OverflowException("Writing past the end of the buffer");
            }
            UnsafeUtility.MemCpy((this.Handle->BufferPointer + this.Handle->Position), value + offset, size);
            this.Handle->Position += size;
        }

        /// <summary>
        /// Write multiple bytes to the stream
        /// </summary>
        /// <param name="value">Value to write</param>
        /// <param name="size">Number of bytes to write</param>
        /// <param name="offset">Offset into the buffer to begin writing</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteBytes(byte[] value, int size = -1, int offset = 0)
        {
            fixed (byte* ptr = value)
            {
                this.WriteBytes(ptr, size == -1 ? value.Length : size, offset);
            }
        }

        /// <summary>
        /// Write multiple bytes to the stream
        ///
        /// "Safe" version - automatically performs bounds checking. Less efficient than bounds checking
        /// for multiple writes at once by calling TryBeginWrite.
        /// </summary>
        /// <param name="value">Value to write</param>
        /// <param name="size">Number of bytes to write</param>
        /// <param name="offset">Offset into the buffer to begin writing</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteBytesSafe(byte[] value, int size = -1, int offset = 0)
        {
            fixed (byte* ptr = value)
            {
                this.WriteBytesSafe(ptr, size == -1 ? value.Length : size, offset);
            }
        }

        /// <summary>
        /// Copy the contents of this writer into another writer.
        /// The contents will be copied from the beginning of this writer to its current position.
        /// They will be copied to the other writer starting at the other writer's current position.
        /// </summary>
        /// <param name="other">Writer to copy to</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void CopyTo(FastBufferWriter other)
        {
            other.WriteBytes(this.Handle->BufferPointer, this.Handle->Position);
        }

        /// <summary>
        /// Copy the contents of another writer into this writer.
        /// The contents will be copied from the beginning of the other writer to its current position.
        /// They will be copied to this writer starting at this writer's current position.
        /// </summary>
        /// <param name="other">Writer to copy to</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void CopyFrom(FastBufferWriter other)
        {
            this.WriteBytes(other.Handle->BufferPointer, other.Handle->Position);
        }

        /// <summary>
        /// Get the size required to write an unmanaged value
        /// </summary>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetWriteSize<T>(in T value) where T : unmanaged
        {
            return sizeof(T);
        }

        /// <summary>
        /// Get the size required to write an unmanaged value of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static unsafe int GetWriteSize<T>() where T : unmanaged
        {
            return sizeof(T);
        }

        /// <summary>
        /// Write a value of any unmanaged type (including unmanaged structs) to the buffer.
        /// It will be copied into the buffer exactly as it exists in memory.
        /// </summary>
        /// <param name="value">The value to copy</param>
        /// <typeparam name="T">Any unmanaged type</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteValue<T>(in T value) where T : unmanaged
        {
            int len = sizeof(T);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
            if (this.Handle->Position + len > this.Handle->AllowedWriteMark)
            {
                throw new OverflowException($"Attempted to write without first calling {nameof(TryBeginWrite)}()");
            }
#endif

            fixed (T* ptr = &value)
            {
                UnsafeUtility.MemCpy(this.Handle->BufferPointer + this.Handle->Position, (byte*)ptr, len);
            }
            this.Handle->Position += len;
        }

        /// <summary>
        /// Write a value of any unmanaged type (including unmanaged structs) to the buffer.
        /// It will be copied into the buffer exactly as it exists in memory.
        ///
        /// "Safe" version - automatically performs bounds checking. Less efficient than bounds checking
        /// for multiple writes at once by calling TryBeginWrite.
        /// </summary>
        /// <param name="value">The value to copy</param>
        /// <typeparam name="T">Any unmanaged type</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteValueSafe<T>(in T value) where T : unmanaged
        {
            int len = sizeof(T);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (this.Handle->InBitwiseContext)
            {
                throw new InvalidOperationException(
                    "Cannot use BufferWriter in bytewise mode while in a bitwise context.");
            }
#endif

            if (!this.TryBeginWriteInternal(len))
            {
                throw new OverflowException("Writing past the end of the buffer");
            }

            fixed (T* ptr = &value)
            {
                UnsafeUtility.MemCpy(this.Handle->BufferPointer + this.Handle->Position, (byte*)ptr, len);
            }
            this.Handle->Position += len;
        }
    }
}