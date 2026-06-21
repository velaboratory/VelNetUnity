using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// Lightweight reader that wraps a byte[] without allocating MemoryStream or BinaryReader.
	/// Format is compatible with BinaryWriter/NetworkWriter for wire protocol compatibility.
	/// </summary>
	public class NetworkReader
	{
		private byte[] buffer;
		private int position;
		private int length;

		public int Position => position;
		public int Remaining => length - position;

		public NetworkReader() { }

		public NetworkReader(byte[] data)
		{
			SetBuffer(data, 0, data.Length);
		}

		public NetworkReader(byte[] data, int offset, int count)
		{
			SetBuffer(data, offset, count);
		}

		public void SetBuffer(byte[] data, int offset, int count)
		{
			buffer = data;
			position = offset;
			length = offset + count;
		}

		#region Primitive Readers

		public byte ReadByte()
		{
			return buffer[position++];
		}

		public bool ReadBool()
		{
			return buffer[position++] != 0;
		}

		public short ReadInt16()
		{
			short value = (short)(buffer[position] | (buffer[position + 1] << 8));
			position += 2;
			return value;
		}

		public int ReadInt32()
		{
			int value = buffer[position]
			            | (buffer[position + 1] << 8)
			            | (buffer[position + 2] << 16)
			            | (buffer[position + 3] << 24);
			position += 4;
			return value;
		}
		public uint ReadUInt32()
		{
			uint value = (uint)(buffer[position]
			            | (buffer[position + 1] << 8)
			            | (buffer[position + 2] << 16)
			            | (buffer[position + 3] << 24));
			position += 4;
			return value;
		}

		public long ReadInt64()
		{
			long value = (long)buffer[position]
			             | ((long)buffer[position + 1] << 8)
			             | ((long)buffer[position + 2] << 16)
			             | ((long)buffer[position + 3] << 24)
			             | ((long)buffer[position + 4] << 32)
			             | ((long)buffer[position + 5] << 40)
			             | ((long)buffer[position + 6] << 48)
			             | ((long)buffer[position + 7] << 56);
			position += 8;
			return value;
		}

		public unsafe float ReadSingle()
		{
			int tmp = buffer[position]
			          | (buffer[position + 1] << 8)
			          | (buffer[position + 2] << 16)
			          | (buffer[position + 3] << 24);
			position += 4;
			return *(float*)&tmp;
		}

		public unsafe double ReadDouble()
		{
			long tmp = (long)buffer[position]
			           | ((long)buffer[position + 1] << 8)
			           | ((long)buffer[position + 2] << 16)
			           | ((long)buffer[position + 3] << 24)
			           | ((long)buffer[position + 4] << 32)
			           | ((long)buffer[position + 5] << 40)
			           | ((long)buffer[position + 6] << 48)
			           | ((long)buffer[position + 7] << 56);
			position += 8;
			return *(double*)&tmp;
		}

		/// <summary>
		/// Reads a string using BinaryWriter-compatible format:
		/// 7-bit encoded length prefix followed by UTF-8 bytes.
		/// </summary>
		public string ReadString()
		{
			int byteCount = Read7BitEncodedInt();
			if (byteCount == 0) return string.Empty;
			string result = Encoding.UTF8.GetString(buffer, position, byteCount);
			position += byteCount;
			return result;
		}

		/// <summary>
		/// Reads a big-endian int32.
		/// </summary>
		public int ReadBigEndianInt()
		{
			int value = (buffer[position] << 24)
			            | (buffer[position + 1] << 16)
			            | (buffer[position + 2] << 8)
			            | buffer[position + 3];
			position += 4;
			return value;
		}

		#endregion

		#region Array Readers

		/// <summary>
		/// Reads count bytes into a new array. Allocates — use ReadBytes(byte[], int, int) when possible.
		/// </summary>
		public byte[] ReadBytes(int count)
		{
			byte[] result = new byte[count];
			System.Array.Copy(buffer, position, result, 0, count);
			position += count;
			return result;
		}

		/// <summary>
		/// Reads count bytes into an existing buffer. Zero-allocation.
		/// </summary>
		public void ReadBytes(byte[] destination, int destOffset, int count)
		{
			System.Array.Copy(buffer, position, destination, destOffset, count);
			position += count;
		}

		/// <summary>
		/// Returns a segment of the underlying buffer without copying.
		/// The segment is only valid until the next SetBuffer call.
		/// </summary>
		public ArraySegment<byte> ReadBytesSegment(int count)
		{
			var segment = new ArraySegment<byte>(buffer, position, count);
			position += count;
			return segment;
		}

		#endregion

		#region Unity Type Readers

		public Vector3 ReadVector3()
		{
			return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
		}

		public Quaternion ReadQuaternion()
		{
			return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
		}

		public Color ReadColor()
		{
			return new Color(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
		}

		public List<int> ReadIntList()
		{
			int count = ReadInt32();
			List<int> list = new List<int>(count);
			for (int i = 0; i < count; i++)
			{
				list.Add(ReadInt32());
			}
			return list;
		}

		public List<string> ReadStringList()
		{
			int count = ReadInt32();
			List<string> list = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				list.Add(ReadString());
			}
			return list;
		}

		#endregion

		private int Read7BitEncodedInt()
		{
			int result = 0;
			int shift = 0;
			byte b;
			do
			{
				b = buffer[position++];
				result |= (b & 0x7F) << shift;
				shift += 7;
			} while ((b & 0x80) != 0);

			return result;
		}
	}
}
