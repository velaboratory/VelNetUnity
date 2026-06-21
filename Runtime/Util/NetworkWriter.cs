using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// Lightweight, reusable byte buffer for writing network messages.
	/// Replaces MemoryStream + BinaryWriter to eliminate per-frame allocations.
	/// Format is compatible with BinaryWriter for wire protocol compatibility.
	/// </summary>
	public class NetworkWriter
	{
		private byte[] buffer;
		private int position;

		public int Position => position;
		public int Length => position;
		public byte[] Buffer => buffer;

		public NetworkWriter(int initialCapacity = 256)
		{
			buffer = new byte[initialCapacity];
		}

		public void Reset()
		{
			position = 0;
		}

		private void EnsureCapacity(int additionalBytes)
		{
			int required = position + additionalBytes;
			if (required <= buffer.Length) return;
			int newSize = System.Math.Max(buffer.Length * 2, required);
			byte[] newBuffer = new byte[newSize];
			System.Array.Copy(buffer, newBuffer, position);
			buffer = newBuffer;
		}

		public ArraySegment<byte> ToArraySegment()
		{
			return new ArraySegment<byte>(buffer, 0, position);
		}

		/// <summary>
		/// Copies the written data to a new byte array. Use sparingly — prefer ToArraySegment.
		/// </summary>
		public byte[] ToArray()
		{
			byte[] result = new byte[position];
			System.Array.Copy(buffer, result, position);
			return result;
		}

		/// <summary>
		/// Reserves space for an int32 (4 bytes) and returns the position.
		/// Use PatchInt or PatchBigEndianInt to fill it in later.
		/// </summary>
		public int ReserveInt()
		{
			int pos = position;
			EnsureCapacity(4);
			position += 4;
			return pos;
		}

		/// <summary>
		/// Writes a little-endian int32 at a previously reserved position.
		/// </summary>
		public void PatchInt(int pos, int value)
		{
			buffer[pos] = (byte)value;
			buffer[pos + 1] = (byte)(value >> 8);
			buffer[pos + 2] = (byte)(value >> 16);
			buffer[pos + 3] = (byte)(value >> 24);
		}

		/// <summary>
		/// Writes a big-endian int32 at a previously reserved position.
		/// </summary>
		public void PatchBigEndianInt(int pos, int value)
		{
			buffer[pos] = (byte)(value >> 24);
			buffer[pos + 1] = (byte)(value >> 16);
			buffer[pos + 2] = (byte)(value >> 8);
			buffer[pos + 3] = (byte)value;
		}

		#region Primitive Writers

		public void Write(byte value)
		{
			EnsureCapacity(1);
			buffer[position++] = value;
		}

		public void Write(bool value)
		{
			EnsureCapacity(1);
			buffer[position++] = value ? (byte)1 : (byte)0;
		}

		public void Write(short value)
		{
			EnsureCapacity(2);
			buffer[position++] = (byte)value;
			buffer[position++] = (byte)(value >> 8);
		}

		public void Write(int value)
		{
			EnsureCapacity(4);
			buffer[position++] = (byte)value;
			buffer[position++] = (byte)(value >> 8);
			buffer[position++] = (byte)(value >> 16);
			buffer[position++] = (byte)(value >> 24);
		}

		public void Write(long value)
		{
			EnsureCapacity(8);
			buffer[position++] = (byte)value;
			buffer[position++] = (byte)(value >> 8);
			buffer[position++] = (byte)(value >> 16);
			buffer[position++] = (byte)(value >> 24);
			buffer[position++] = (byte)(value >> 32);
			buffer[position++] = (byte)(value >> 40);
			buffer[position++] = (byte)(value >> 48);
			buffer[position++] = (byte)(value >> 56);
		}

		public unsafe void Write(float value)
		{
			EnsureCapacity(4);
			uint tmp = *(uint*)&value;
			buffer[position++] = (byte)tmp;
			buffer[position++] = (byte)(tmp >> 8);
			buffer[position++] = (byte)(tmp >> 16);
			buffer[position++] = (byte)(tmp >> 24);
		}

		public unsafe void Write(double value)
		{
			EnsureCapacity(8);
			ulong tmp = *(ulong*)&value;
			buffer[position++] = (byte)tmp;
			buffer[position++] = (byte)(tmp >> 8);
			buffer[position++] = (byte)(tmp >> 16);
			buffer[position++] = (byte)(tmp >> 24);
			buffer[position++] = (byte)(tmp >> 32);
			buffer[position++] = (byte)(tmp >> 40);
			buffer[position++] = (byte)(tmp >> 48);
			buffer[position++] = (byte)(tmp >> 56);
		}

		private static int Get7BitEncodedIntSize(int value)
{
    if (value < 0x80) return 1;
    if (value < 0x4000) return 2;
    if (value < 0x200000) return 3;
    if (value < 0x10000000) return 4;
    return 5;
}
private static int Write7BitEncodedInt(byte[] buffer, int offset, int value)
 {
     int start = offset;

     while (value >= 0x80)
     {
         buffer[offset++] = (byte)(value | 0x80);
         value >>= 7;
     }

     buffer[offset++] = (byte)value;
     return offset - start;
 }

		/// <summary>
		/// Writes a string using BinaryWriter-compatible format:
		/// 7-bit encoded length prefix followed by UTF-8 bytes.
		/// </summary>
		public void Write(string value)
		{
			if (value == null) value = string.Empty;
			int byteCount = Encoding.UTF8.GetByteCount(value);
			Write7BitEncodedInt(byteCount);

			EnsureCapacity(byteCount);

			position += Encoding.UTF8.GetBytes(
				value.AsSpan(),
				buffer.AsSpan(position)
			);
			/*
			int byteCount = Encoding.UTF8.GetByteCount(value);
			Write7BitEncodedInt(byteCount);
			EnsureCapacity(byteCount);
			Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, position);
			position += byteCount;
			*/
		}

		/// <summary>
		/// Big-endian int32 write (no allocation, unlike the old WriteBigEndian extension).
		/// </summary>
		public void WriteBigEndianInt(int value)
		{
			EnsureCapacity(4);
			buffer[position++] = (byte)(value >> 24);
			buffer[position++] = (byte)(value >> 16);
			buffer[position++] = (byte)(value >> 8);
			buffer[position++] = (byte)value;
		}

		#endregion

		#region Array/Buffer Writers

		public void Write(byte[] data)
		{
			if (data == null || data.Length == 0) return;
			EnsureCapacity(data.Length);
			System.Array.Copy(data, 0, buffer, position, data.Length);
			position += data.Length;
		}

		public void Write(byte[] data, int offset, int count)
		{
			if (count <= 0) return;
			EnsureCapacity(count);
			System.Array.Copy(data, offset, buffer, position, count);
			position += count;
		}

		public void Write(ArraySegment<byte> segment)
		{
			if (segment.Count <= 0) return;
			EnsureCapacity(segment.Count);
			System.Array.Copy(segment.Array, segment.Offset, buffer, position, segment.Count);
			position += segment.Count;
		}

		#endregion

		#region Unity Type Writers

		public void Write(Vector3 v)
		{
			Write(v.x);
			Write(v.y);
			Write(v.z);
		}

		public void Write(Quaternion q)
		{
			Write(q.x);
			Write(q.y);
			Write(q.z);
			Write(q.w);
		}

		public void Write(Color c)
		{
			Write(c.r);
			Write(c.g);
			Write(c.b);
			Write(c.a);
		}

		public void Write(List<int> l)
		{
			Write(l.Count);
			for (int i = 0; i < l.Count; i++)
			{
				Write(l[i]);
			}
		}

		public void Write(List<string> l)
		{
			Write(l.Count);
			for (int i = 0; i < l.Count; i++)
			{
				Write(l[i]);
			}
		}

		#endregion

		/// <summary>
		/// 7-bit encoded integer, matching BinaryWriter's format for string length prefixes.
		/// </summary>
		private void Write7BitEncodedInt(int value)
		{
			uint v = (uint)value;
			while (v >= 0x80)
			{
				Write((byte)(v | 0x80));
				v >>= 7;
			}
			Write((byte)v);
		}
	}
}
