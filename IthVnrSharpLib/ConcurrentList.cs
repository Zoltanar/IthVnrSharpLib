using System;
using System.Collections.Generic;
using System.Linq;
// ReSharper disable UnusedMember.Global

namespace IthVnrSharpLib
{
	public abstract class ConcurrentListBase<T>
	{
		public object SyncRoot { get; } = new();
		protected readonly List<T> Items = new();

		public int Count
		{
			get
			{
				lock (SyncRoot)
				{
					return Items.Count;
				}
			}
		}

		public void Clear()
		{
			lock (SyncRoot)
			{
				Items.Clear();
			}
		}

		public void AddRange(IEnumerable<T> collection)
		{
			lock (SyncRoot)
			{
				Items.AddRange(collection);
			}
		}
	}

	public class ConcurrentList<T> : ConcurrentListBase<T>
	{
		public T[] ArrayCopy()
		{
			lock (SyncRoot)
			{
				return Items.ToArray();
			}
		}
		
		public void Add(T item)
		{
			lock (SyncRoot)
			{
				Items.Add(item);
			}
		}
	}

	public class ConcurrentArrayList<T> : ConcurrentListBase<T[]>
	{
		public int TrimAt { get; set; }
		public int TrimCount { get; set; }

		public ConcurrentArrayList()
		{}

		/// <summary>
		/// Auto remove the first trimCount values when collection reaches trimAt items.
		/// </summary>
		/// <param name="trimAt"></param>
		/// <param name="trimCount"></param>
		public ConcurrentArrayList(int trimAt, int trimCount)
		{
			if(trimCount > trimAt) throw new ArgumentException("trimCount must be lower than trimAt.");
			TrimAt = trimAt;
			TrimCount = trimCount;
			Items.Capacity = trimAt;
		}

		/// <summary>
		/// Make sure to wrap in lock (SyncRoot) when using.
		/// </summary>
		// ReSharper disable once InconsistentlySynchronizedField
		public IReadOnlyList<T[]> ReadOnlyList => Items.AsReadOnly();
		
		public int AggregateCount
		{
			get
			{
				lock (SyncRoot)
				{
					return Items.Sum(x => x.Length);
				}
			}
		}
		
		public void Add(T[] item)
		{
			lock (SyncRoot)
			{
				Items.Add(item);
				if (TrimAt > 0 && Items.Count == TrimAt) Items.RemoveRange(0, TrimCount);
			}
		}

		public T[] ToAggregateArray()
		{
			lock (SyncRoot)
			{
				return Items.SelectMany(x=>x).ToArray();
			}
		}
	}
}