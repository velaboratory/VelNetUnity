using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VelNet
{
	public static class BinaryWriterExtensions
	{
		public static void Write(this BinaryWriter writer, Vector3 v)
		{
			writer.Write(v.x);
			writer.Write(v.y);
			writer.Write(v.z);
		}

		public static void Write(this BinaryWriter writer, Quaternion q)
		{
			writer.Write(q.x);
			writer.Write(q.y);
			writer.Write(q.z);
			writer.Write(q.w);
		}

		public static Vector3 ReadVector3(this BinaryReader reader)
		{
			return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		}

		public static Quaternion ReadQuaternion(this BinaryReader reader)
		{
			return new Quaternion(
				reader.ReadSingle(), 
				reader.ReadSingle(), 
				reader.ReadSingle(), 
				reader.ReadSingle()
			);
		}

		/// <summary>
		/// Compresses the list of bools into bytes using a bitmask
		/// </summary>
		public static byte[] GetBitmasks(this IEnumerable<bool> bools)
		{
			List<bool> values = bools.ToList(); 
			List<byte> bytes = new List<byte>();
			for (int b = 0; b < Mathf.Ceil(values.Count / 8f); b++)
			{
				byte currentByte = 0;
				for (int bit = 0; bit < 8; bit++)
				{
					if (values.Count > b * 8 + bit)
					{
						currentByte |= (byte)((values[b * 8 + bit] ? 1 : 0) << bit);
					}
				}

				bytes.Add(currentByte);
			}

			return bytes.ToArray();
		}
		
		public static List<bool> GetBitmaskValues(this IEnumerable<byte> bytes)
		{
			List<bool> l = new List<bool>();
			foreach (byte b in bytes)
			{
				l.AddRange(b.GetBitmaskValues());
			}

			return l;
		}
		
		public static List<bool> GetBitmaskValues(this byte b)
		{
			List<bool> l = new List<bool>();
			for (int i = 0; i < 8; i++)
			{
				l.Add(b.GetBitmaskValue(i));
			}

			return l;
		}
		
		public static bool GetBitmaskValue(this byte b, int index)
		{
			return (b & (1 << index)) != 0;
		}
		
	}
}