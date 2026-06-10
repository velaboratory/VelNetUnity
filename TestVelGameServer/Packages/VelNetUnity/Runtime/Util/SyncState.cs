using System;
using System.Collections.Generic;
using UnityEngine;

namespace VelNet
{
	public abstract class SyncState : NetworkComponent, IPackState
	{
		[Tooltip("Send rate of this object. This caps out at the framerate of the game.")]
		public float serializationRateHz = 30;

		/// <summary>
		/// On-change compression: state is only sent while it is CHANGING (streamed
		/// unreliable/UDP), and when it stops changing the final state is sent ONCE
		/// reliably (TCP) so every client is guaranteed to converge — after that a
		/// parked object costs zero bandwidth. There is no periodic keepalive: with
		/// thousands of parked drawings the old 2s re-send was real money in relay
		/// bandwidth. Late joiners get state via PackState snapshots instead.
		/// When false, the state is sent every tick regardless of change (the
		/// continuous stream is its own loss recovery).
		/// </summary>
		public bool hybridOnChangeCompression = true;

		[Tooltip("Force the CHANGE stream over TCP instead of UDP (the final settle-confirm " +
		         "is always TCP). Leave OFF: a lost change packet self-heals on the next tick " +
		         "(~33ms), and high-rate state on the TCP stream delays real events behind a " +
		         "stalled socket on a congested uplink. Unreliable sends automatically fall " +
		         "back to TCP when UDP is unavailable.")]
		public bool sendReliable = false;

		private byte[] lastSentBytes;
		private int lastSentLength;
		// Per-instance schedule for the shared ticker (replaces the per-object coroutine).
		private double nextSerializeTime;
		// Settle-confirm bookkeeping: after the change stream goes quiet for
		// SETTLE_CONFIRM_TICKS ticks, re-send the (unchanged) state once reliably. The
		// small delay lets any straggling unreliable packet land first, so a stale UDP
		// update can't arrive after — and silently undo — the reliable final state.
		private const int SETTLE_CONFIRM_TICKS = 2;
		private int unchangedTicks;
		private bool pendingReliableConfirm;

		/// <summary>
		/// True while ReceiveState is being invoked from a packed-state snapshot
		/// (late-join InstantiateWithState / ForceState) rather than the live stream.
		/// Subclasses should APPLY such state directly (e.g. teleport instead of
		/// interpolating): there is no stream context to smooth from.
		/// </summary>
		protected bool receivingPackedState;

		private NetworkWriter writer;
		private NetworkReader reader;

		// ---- centralised ticking -------------------------------------------------
		// One hidden runner iterates every SyncState once per frame instead of running
		// one coroutine per component: with hundreds of owned synced objects the
		// per-object coroutine resumption (plus a WaitForSeconds allocation every
		// iteration) cost milliseconds per frame on Quest. Wire payloads are unchanged.
		private static readonly List<SyncState> all = new List<SyncState>();
		private static SyncStateTicker ticker;

		protected virtual void Awake()
		{
			writer = new NetworkWriter();
			reader = new NetworkReader();

			// Stagger first sends so hundreds of objects created together (scene load,
			// late join) don't all serialize on the same frame.
			nextSerializeTime = Time.timeAsDouble +
			                    (GetInstanceID() & 31) / 31.0 / Mathf.Max(1f, serializationRateHz);
			all.Add(this);
			if (ticker == null)
			{
				GameObject go = new GameObject("VelNet SyncState Ticker") { hideFlags = HideFlags.HideInHierarchy };
				DontDestroyOnLoad(go);
				ticker = go.AddComponent<SyncStateTicker>();
			}
		}

		protected virtual void OnDestroy()
		{
			// Derived classes may declare their own OnDestroy (shadowing this one); the
			// ticker also prunes destroyed entries, so a missed remove is harmless.
			all.Remove(this);
		}

		internal static void TickAll()
		{
			double now = Time.timeAsDouble;
			for (int i = all.Count - 1; i >= 0; i--)
			{
				SyncState s = all[i];
				if (s == null)
				{
					all.RemoveAt(i); // destroyed without deregistering (shadowed OnDestroy)
					continue;
				}

				try
				{
					if (!s.IsMine || !s.enabled || !s.gameObject.activeInHierarchy) continue;
					if (now < s.nextSerializeTime) continue;
					s.nextSerializeTime = now + 1.0 / Mathf.Max(1f, s.serializationRateHz);

					s.writer.Reset();
					s.SendState(s.writer);

					int newLength = s.writer.Length;
					byte[] newBuffer = s.writer.Buffer;

					if (!s.hybridOnChangeCompression)
					{
						// Continuous mode: every tick, stream transport.
						s.SendBytes(newBuffer, 0, newLength, s.sendReliable);
						continue;
					}

					if (!BinaryWriterExtensions.BytesSame(s.lastSentBytes, s.lastSentLength, newBuffer, newLength))
					{
						// Changing: stream it (unreliable by default — the next tick heals a loss).
						s.SendBytes(newBuffer, 0, newLength, s.sendReliable);
						if (s.lastSentBytes == null || s.lastSentBytes.Length < newLength)
						{
							s.lastSentBytes = new byte[newLength];
						}

						Array.Copy(newBuffer, s.lastSentBytes, newLength);
						s.lastSentLength = newLength;
						s.unchangedTicks = 0;
						s.pendingReliableConfirm = true;
					}
					else if (s.pendingReliableConfirm && ++s.unchangedTicks >= SETTLE_CONFIRM_TICKS)
					{
						// Stopped changing: one reliable send of the final state, then silence.
						s.SendBytes(newBuffer, 0, newLength, true);
						s.pendingReliableConfirm = false;
					}
				}
				catch (Exception e)
				{
					Debug.LogError(e);
				}
			}
		}

		public override void ReceiveBytes(NetworkReader networkReader)
		{
			// Live stream: apply with interpolation context.
			ReceiveState(networkReader);
		}

		protected abstract void SendState(NetworkWriter networkWriter);

		protected abstract void ReceiveState(NetworkReader networkReader);

		public void PackState(NetworkWriter networkWriter)
		{
			SendState(networkWriter);
		}

		public void UnpackState(NetworkReader networkReader)
		{
			// IPackState path: VelNet calls this for packed-state SNAPSHOTS only
			// (late-join InstantiateWithState / ForceState) — the live stream goes
			// through ReceiveBytes above. Same payload bytes; subclasses see
			// receivingPackedState and should apply instantly (teleport, not lerp).
			receivingPackedState = true;
			try
			{
				ReceiveState(networkReader);
			}
			finally
			{
				receivingPackedState = false;
			}
		}

		public void ForceSync()
		{
			writer.Reset();
			SendState(writer);
			SendBytes(writer.Buffer, 0, writer.Length);
		}
	}

	/// <summary>
	/// Hidden singleton that drives <see cref="SyncState.TickAll"/>. LateUpdate is the
	/// closest point to where the old per-object coroutines resumed (after Update), so
	/// serialized state is at least as fresh as before.
	/// </summary>
	internal class SyncStateTicker : MonoBehaviour
	{
		private void LateUpdate()
		{
			SyncState.TickAll();
		}
	}
}
