// This file is part of the C5 Generic Collection Library for C# and CLI
// See https://github.com/sestoft/C5/blob/master/LICENSE.txt for licensing details.

using System;
using SCG = System.Collections.Generic;

namespace C5
{
    /// <summary>
    /// A set collection class based on linear hashing
    /// </summary>
    [Serializable]
    public class HashSet<T> : CollectionBase<T>, ICollection<T>, SCG.ISet<T>
    {
        #region Feature
        /// <summary>
        /// Enum class to assist printing of compilation alternatives.
        /// </summary>
        [Flags]
        public enum Feature : short
        {
            /// <summary>
            /// Nothing
            /// </summary>
            Dummy = 0,
            /// <summary>
            /// Buckets are of reference type
            /// </summary>
            RefTypeBucket = 1,
            /// <summary>
            /// Primary buckets are of value type
            /// </summary>
            ValueTypeBucket = 2,
            /// <summary>
            /// Using linear probing to resolve index clashes
            /// </summary>
            LinearProbing = 4,
            /// <summary>
            /// Shrink table when very sparsely filled
            /// </summary>
            ShrinkTable = 8,
            /// <summary>
            /// Use chaining to resolve index clashes
            /// </summary>
            Chaining = 16,
            /// <summary>
            /// Use hash function on item hash code
            /// </summary>
            InterHashing = 32,
            /// <summary>
            /// Use a universal family of hash functions on item hash code
            /// </summary>
            RandomInterHashing = 64
        }


        private static readonly Feature features = Feature.Dummy
                                          | Feature.RefTypeBucket
                                          | Feature.Chaining
                                          | Feature.RandomInterHashing;


        /// <summary>
        /// Show which implementation features was chosen at compilation time
        /// </summary>
        public static Feature Features => features;

        #endregion
        #region Fields
        private int indexmask;
        private int bits;
        private int bitsc;
        private readonly int origbits;
        private int lastchosen;
        private Bucket?[] table;
        private readonly double fillfactor = 0.66;
        private int resizethreshhold;

        private static readonly Random Random = new Random();
        private uint _randomhashfactor;

        #endregion

        #region Events

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        public override EventType ListenableEvents => EventType.Basic;

        #endregion

        #region Bucket nested class(es)
        [Serializable]
        private class Bucket
        {
            internal T item;

            internal int hashval; //Cache!

            internal Bucket? overflow;

            internal Bucket(T item, int hashval, Bucket? overflow)
            {
                this.item = item;
                this.hashval = hashval;
                this.overflow = overflow;
            }
        }

        #endregion

        #region Basic Util

        private bool Equals(T i1, T i2) { return itemequalityComparer.Equals(i1, i2); }

        private int GetHashCode(T item) { return itemequalityComparer.GetHashCode(item); }

        private int Hv2i(int hashval)
        {
            return (int)(((uint)hashval * _randomhashfactor) >> bitsc);
        }

        private void Expand()
        {
            Logger.Log(string.Format(string.Format("Expand to {0} bits", bits + 1)));
            Resize(bits + 1);
        }

        /*
        void shrink()
        {
            if (bits > 3)
            {
                Logger.Log(string.Format(string.Format("Shrink to {0} bits", bits - 1)));
                resize(bits - 1);
            }
        } */


        private void Resize(int bits)
        {
            Logger.Log(string.Format(string.Format("Resize to {0} bits", bits)));
            this.bits = bits;
            bitsc = 32 - bits;
            indexmask = (1 << bits) - 1;

            Bucket[] newtable = new Bucket[indexmask + 1];

            for (int i = 0, s = table.Length; i < s; i++)
            {
                Bucket? b = table[i];

                while (b != null)
                {
                    int j = Hv2i(b.hashval);

                    newtable[j] = new Bucket(b.item, b.hashval, newtable[j]);
                    b = b.overflow;
                }

            }

            table = newtable;
            resizethreshhold = (int)(table.Length * fillfactor);
            Logger.Log(string.Format(string.Format("Resize to {0} bits done", bits)));
        }

        /// <summary>
        /// Search for an item equal (according to itemequalityComparer) to the supplied item.  
        /// </summary>
        /// <param name="item"></param>
        /// <param name="add">If true, add item to table if not found.</param>
        /// <param name="update">If true, update table entry if item found.</param>
        /// <param name="raise">If true raise events</param>
        /// <returns>True if found</returns>
        private bool SearchOrAdd(ref T item, bool add, bool update, bool raise)
        {

            int hashval = GetHashCode(item);
            int i = Hv2i(hashval);
            Bucket? b = table[i];
            Bucket? bold = null;

            if (b != null)
            {
                while (b != null)
                {
                    T olditem = b.item;
                    if (Equals(olditem, item))
                    {
                        if (update)
                        {
                            b.item = item;
                        }
                        if (raise && update)
                        {
                            RaiseForUpdate(item, olditem);
                        }
                        // bug20071112:
                        item = olditem;
                        return true;
                    }

                    bold = b;
                    b = b.overflow;
                }

                if (!add)
                {
                    goto notfound;
                }

                bold!.overflow = new Bucket(item, hashval, null);
            }
            else
            {
                if (!add)
                {
                    goto notfound;
                }

                table[i] = new Bucket(item, hashval, null);
            }
            size++;
            if (size > resizethreshhold)
            {
                Expand();
            }

        notfound:
            if (raise && add)
            {
                RaiseForAdd(item);
            }

            if (update)
            {
                item = default;
            }

            return false;
        }


        private bool Remove(ref T item)
        {

            if (size == 0)
            {
                return false;
            }

            int hashval = GetHashCode(item);
            int index = Hv2i(hashval);
            Bucket? b = table[index], bold;

            if (b == null)
            {
                return false;
            }

            if (Equals(item, b.item))
            {
                //ref
                item = b.item;
                table[index] = b.overflow;
            }
            else
            {
                bold = b;
                b = b.overflow;
                while (b != null && !Equals(item, b.item))
                {
                    bold = b;
                    b = b.overflow;
                }

                if (b == null)
                {
                    return false;
                }

                //ref
                item = b.item;
                bold.overflow = b.overflow;
            }
            size--;

            return true;
        }


        private void ClearInner()
        {
            bits = origbits;
            bitsc = 32 - bits;
            indexmask = (1 << bits) - 1;
            size = 0;
            table = new Bucket[indexmask + 1];
            resizethreshhold = (int)(table.Length * fillfactor);
        }

        #endregion

        #region Constructors
        /// <summary>
        /// Create a hash set with natural item equalityComparer and default fill threshold (66%)
        /// and initial table size (16).
        /// </summary>
        public HashSet()
            : this(EqualityComparer<T>.Default) { }


        /// <summary>
        /// Create a hash set with external item equalityComparer and default fill threshold (66%)
        /// and initial table size (16).
        /// </summary>
        /// <param name="itemequalityComparer">The external item equalitySCG.Comparer</param>
        public HashSet(SCG.IEqualityComparer<T> itemequalityComparer)
            : this(16, itemequalityComparer) { }


        /// <summary>
        /// Create a hash set with external item equalityComparer and default fill threshold (66%)
        /// </summary>
        /// <param name="capacity">Initial table size (rounded to power of 2, at least 16)</param>
        /// <param name="itemequalityComparer">The external item equalitySCG.Comparer</param>
        public HashSet(int capacity, SCG.IEqualityComparer<T> itemequalityComparer)
            : this(capacity, 0.66, itemequalityComparer) { }


        /// <summary>
        /// Create a hash set with external item equalityComparer.
        /// </summary>
        /// <param name="capacity">Initial table size (rounded to power of 2, at least 16)</param>
        /// <param name="fill">Fill threshold (in range 10% to 90%)</param>
        /// <param name="itemequalityComparer">The external item equalitySCG.Comparer</param>
        public HashSet(int capacity, double fill, SCG.IEqualityComparer<T> itemequalityComparer)
            : base(itemequalityComparer)
        {
            _randomhashfactor = (Debug.UseDeterministicHashing) ? 1529784659 : (2 * (uint)Random.Next() + 1) * 1529784659;

            if (fill < 0.1 || fill > 0.9)
            {
                throw new ArgumentException("Fill outside valid range [0.1, 0.9]");
            }

            if (capacity <= 0)
            {
                throw new ArgumentException("Capacity must be non-negative");
            }
            //this.itemequalityComparer = itemequalityComparer;
            origbits = 4;
            while (capacity - 1 >> origbits > 0)
            {
                origbits++;
            }

            ClearInner();
        }



        #endregion

        #region IEditableCollection<T> Members

        /// <summary>
        /// The complexity of the Contains operation
        /// </summary>
        /// <value>Always returns Speed.Constant</value>
        public virtual Speed ContainsSpeed => Speed.Constant;

        /// <summary>
        /// Check if an item is in the set 
        /// </summary>
        /// <param name="item">The item to look for</param>
        /// <returns>True if set contains item</returns>
        public virtual bool Contains(T item) { return SearchOrAdd(ref item, false, false, false); }


        /// <summary>
        /// Check if an item (collection equal to a given one) is in the set and
        /// if so report the actual item object found.
        /// </summary>
        /// <param name="item">On entry, the item to look for.
        /// On exit the item found, if any</param>
        /// <returns>True if set contains item</returns>
        public virtual bool Find(ref T item) { return SearchOrAdd(ref item, false, false, false); }


        /// <summary>
        /// Check if an item (collection equal to a given one) is in the set and
        /// if so replace the item object in the set with the supplied one.
        /// </summary>
        /// <param name="item">The item object to update with</param>
        /// <returns>True if item was found (and updated)</returns>
        public virtual bool Update(T item)
        { UpdateCheck(); return SearchOrAdd(ref item, false, true, true); }

        /// <summary>
        /// Check if an item (collection equal to a given one) is in the set and
        /// if so replace the item object in the set with the supplied one.
        /// </summary>
        /// <param name="item">The item object to update with</param>
        /// <param name="olditem"></param>
        /// <returns>True if item was found (and updated)</returns>
        public virtual bool Update(T item, out T olditem)
        { UpdateCheck(); olditem = item; return SearchOrAdd(ref olditem, false, true, true); }


        /// <summary>
        /// Check if an item (collection equal to a given one) is in the set.
        /// If found, report the actual item object in the set,
        /// else add the supplied one.
        /// </summary>
        /// <param name="item">On entry, the item to look for or add.
        /// On exit the actual object found, if any.</param>
        /// <returns>True if item was found</returns>
        public virtual bool FindOrAdd(ref T item)
        { UpdateCheck(); return SearchOrAdd(ref item, true, false, true); }


        /// <summary>
        /// Check if an item (collection equal to a supplied one) is in the set and
        /// if so replace the item object in the set with the supplied one; else
        /// add the supplied one.
        /// </summary>
        /// <param name="item">The item to look for and update or add</param>
        /// <returns>True if item was updated</returns>
        public virtual bool UpdateOrAdd(T item)
        { UpdateCheck(); return SearchOrAdd(ref item, true, true, true); }


        /// <summary>
        /// Check if an item (collection equal to a supplied one) is in the set and
        /// if so replace the item object in the set with the supplied one; else
        /// add the supplied one.
        /// </summary>
        /// <param name="item">The item to look for and update or add</param>
        /// <param name="olditem"></param>
        /// <returns>True if item was updated</returns>
        public virtual bool UpdateOrAdd(T item, out T olditem)
        { UpdateCheck(); olditem = item; return SearchOrAdd(ref olditem, true, true, true); }


        /// <summary>
        /// Remove an item from the set
        /// </summary>
        /// <param name="item">The item to remove</param>
        /// <returns>True if item was (found and) removed </returns>
        public override bool Remove(T item)
        {
            UpdateCheck();
            if (Remove(ref item))
            {
                RaiseForRemove(item);
                return true;
            }
            else
            {
                return false;
            }
        }


        /// <summary>
        /// Remove an item from the set, reporting the actual matching item object.
        /// </summary>
        /// <param name="item">The value to remove.</param>
        /// <param name="removeditem">The removed value.</param>
        /// <returns>True if item was found.</returns>
        public virtual bool Remove(T item, out T removeditem)
        {
            UpdateCheck();
            removeditem = item;
            if (Remove(ref removeditem))
            {
                RaiseForRemove(removeditem);
                return true;
            }
            else
            {
                return false;
            }
        }


        /// <summary>
        /// Remove all items in a supplied collection from this set.
        /// </summary>
        /// <param name="items">The items to remove.</param>
        public virtual void RemoveAll(SCG.IEnumerable<T> items)
        {
            UpdateCheck();
            RaiseForRemoveAllHandler raiseHandler = new RaiseForRemoveAllHandler(this);
            bool raise = raiseHandler.MustFire;
            T jtem;
            foreach (var item in items)
            {
                jtem = item; if (Remove(ref jtem) && raise)
                {
                    raiseHandler.Remove(jtem);
                }
            }

            if (raise)
            {
                raiseHandler.Raise();
            }
        }

        /// <summary>
        /// Remove all items from the set, resetting internal table to initial size.
        /// </summary>
        public override void Clear()
        {
            UpdateCheck();
            int oldsize = size;
            ClearInner();
            if (ActiveEvents != 0 && oldsize > 0)
            {
                RaiseCollectionCleared(true, oldsize);
                RaiseCollectionChanged();
            }
        }


        /// <summary>
        /// Remove all items *not* in a supplied collection from this set.
        /// </summary>
        /// <param name="items">The items to retain</param>
        public virtual void RetainAll(SCG.IEnumerable<T> items)
        {
            UpdateCheck();

            HashSet<T> aux = new HashSet<T>(EqualityComparer);

            //This only works for sets:
            foreach (var item in items)
            {
                if (Contains(item))
                {
                    T jtem = item;

                    aux.SearchOrAdd(ref jtem, true, false, false);
                }
            }

            if (size == aux.size)
            {
                return;
            }

            CircularQueue<T>? wasRemoved = null;
            if ((ActiveEvents & EventType.Removed) != 0)
            {
                wasRemoved = new CircularQueue<T>();
                foreach (T item in this)
                {
                    if (!aux.Contains(item))
                    {
                        wasRemoved.Enqueue(item);
                    }
                }
            }

            table = aux.table;
            size = aux.size;

            indexmask = aux.indexmask;
            resizethreshhold = aux.resizethreshhold;
            bits = aux.bits;
            bitsc = aux.bitsc;

            _randomhashfactor = aux._randomhashfactor;

            if ((ActiveEvents & EventType.Removed) != 0)
            {
                RaiseForRemoveAll(wasRemoved);
            }
            else if ((ActiveEvents & EventType.Changed) != 0)
            {
                RaiseCollectionChanged();
            }
        }

        /// <summary>
        /// Check if all items in a supplied collection is in this set
        /// (ignoring multiplicities). 
        /// </summary>
        /// <param name="items">The items to look for.</param>
        /// <returns>True if all items are found.</returns>
        public virtual bool ContainsAll(SCG.IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                if (!Contains(item))
                {
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// Create an array containing all items in this set (in enumeration order).
        /// </summary>
        /// <returns>The array</returns>
        public override T[] ToArray()
        {
            T[] res = new T[size];
            int index = 0;

            for (int i = 0; i < table.Length; i++)
            {
                Bucket? b = table[i];
                while (b != null)
                {
                    res[index++] = b.item;
                    b = b.overflow;
                }
            }

            System.Diagnostics.Debug.Assert(size == index);
            return res;
        }


        /// <summary>
        /// Count the number of times an item is in this set (either 0 or 1).
        /// </summary>
        /// <param name="item">The item to look for.</param>
        /// <returns>1 if item is in set, 0 else</returns>
        public virtual int ContainsCount(T item) { return Contains(item) ? 1 : 0; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public virtual ICollectionValue<T> UniqueItems() { return this; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public virtual ICollectionValue<System.Collections.Generic.KeyValuePair<T, int>> ItemMultiplicities()
        {
            return new MultiplicityOne<T>(this);
        }

        /// <summary>
        /// Remove all (at most 1) copies of item from this set.
        /// </summary>
        /// <param name="item">The item to remove</param>
        public virtual void RemoveAllCopies(T item) { Remove(item); }

        #endregion

        #region IEnumerable<T> Members


        /// <summary>
        /// Choose some item of this collection. 
        /// </summary>
        /// <exception cref="NoSuchItemException">if collection is empty.</exception>
        /// <returns></returns>
        public override T Choose()
        {
            int len = table.Length;
            if (size == 0)
            {
                throw new NoSuchItemException();
            }

            do { if (++lastchosen >= len) { lastchosen = 0; } } while (table[lastchosen] == null);

            return (table[lastchosen])!.item;
        }

        /// <summary>
        /// Create an enumerator for this set.
        /// </summary>
        /// <returns>The enumerator</returns>
        public override SCG.IEnumerator<T> GetEnumerator()
        {
            int index = -1;
            int mystamp = stamp;
            int len = table.Length;

            Bucket? b = null;

            while (true)
            {
                if (mystamp != stamp)
                {
                    throw new CollectionModifiedException();
                }

                if (b == null || b.overflow == null)
                {
                    do
                    {
                        if (++index >= len)
                        {
                            yield break;
                        }
                    } while (table[index] == null);

                    b = table[index];
                    yield return b!.item;
                }
                else
                {
                    b = b.overflow;
                    yield return b.item;
                }
            }
        }

        #endregion

        #region ISet<T> Members

        /// <summary>
        /// Modifies the current <see cref="HashSet{T}"/> object to contain all elements that are present in itself, the specified collection, or both.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual void UnionWith(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            AddAll(other);
        }

        /// <summary>
        /// Modifies the current <see cref="HashSet{T}"/> object so that it contains only elements that are also in a specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual void IntersectWith(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // intersection of anything with empty set is empty set, so return if count is 0
            if (this.size == 0)
            {
                return;
            }

            // if other is empty, intersection is empty set; remove all elements and we're done
            // can only figure this out if implements ICollection<T>. (IEnumerable<T> has no count)
            if (other is SCG.ICollection<T> otherAsCollection)
            {
                if (otherAsCollection.Count == 0)
                {
                    Clear();
                    return;
                }

                // faster if other is a hashset using same equality comparer; so check 
                // that other is a hashset using the same equality comparer.
                if (other is TreeSet<T> otherAsSet && AreEqualityComparersEqual(this, otherAsSet))
                {
                    IntersectWithHashSetWithSameEC(otherAsSet);
                    return;
                }
            }

            IntersectWithEnumerable(other);
        }

        /// <summary>
        /// Removes all elements in the specified collection from the current <see cref="HashSet{T}"/> object.
        /// </summary>
        /// <param name="other">The collection of items to remove from the set.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual void ExceptWith(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // this is already the enpty set; return
            if (this.size == 0)
                return;

            // special case if other is this; a set minus itself is the empty set
            if (other == this)
            {
                Clear();
                return;
            }

            // remove every element in other from this
            foreach (T element in other)
            {
                Remove(element);
            }
        }

        /// <summary>
        /// Modifies the current set so that it contains only elements that are present either in the current 
        /// <see cref="HashSet{T}"/> object or in the specified collection, but not both.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual void SymmetricExceptWith(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // if set is empty, then symmetric difference is other
            if (this.size == 0)
            {
                UnionWith(other);
                return;
            }

            // special case this; the symmetric difference of a set with itself is the empty set
            if (other == this)
            {
                Clear();
                return;
            }

            // If other is a HashSet, it has unique elements according to its equality comparer,
            // but if they're using different equality comparers, then assumption of uniqueness
            // will fail. So first check if other is a hashset using the same equality comparer;
            // symmetric except is a lot faster and avoids bit array allocations if we can assume
            // uniqueness
            if (other is SCG.ICollection<T> otherAsCollection && AreEqualityComparersEqual(this, otherAsCollection))
            {
                SymmetricExceptWithUniqueHashSet(otherAsCollection);
            }
            else
            {
                var temp = new SCG.HashSet<T>(other, EqualityComparer);
                temp.ExceptWith(this);
                ExceptWith(other);
                UnionWith(temp);
            }
        }

        /// <summary>
        /// Determines whether a <see cref="HashSet{T}"/> object is a subset of the specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns><c>true</c> if the <see cref="HashSet{T}"/> object is a subset of other; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual bool IsSubsetOf(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            if (this.size == 0)
            {
                return true;
            }

            // faster if other has unique elements according to this equality comparer; so check 
            // that other is a hashset using the same equality comparer.
            if (other is SCG.ICollection<T> otherAsCollection && AreEqualityComparersEqual(this, otherAsCollection))
            {
                // if this has more elements then it can't be a subset
                if (this.size > otherAsCollection.Count)
                {
                    return false;
                }

                // already checked that we're using same equality comparer. simply check that 
                // each element in this is contained in other.
                return IsSubsetOfHashSetWithSameEC(otherAsCollection);
            }

            // we just need to return true if the other set
            // contains all of the elements of the this set,
            // but we need to use the comparison rules of the current set.
            this.CheckUniqueAndUnfoundElements(other, false, out int uniqueCount, out int _);
            return uniqueCount == this.size;
        }

        /// <summary>
        /// Determines whether a <see cref="HashSet{T}"/> object is a superset of the specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns><c>true</c> if the <see cref="HashSet{T}"/> object is a superset of other; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual bool IsSupersetOf(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // try to fall out early based on counts
            if (other is SCG.ICollection<T> otherAsCollection)
            {
                // if other is the empty set then this is a superset
                if (otherAsCollection.Count == 0)
                    return true;

                // try to compare based on counts alone if other is a hashset with
                // same equality comparer
                if (AreEqualityComparersEqual(this, otherAsCollection))
                {
                    if (otherAsCollection.Count > this.size)
                    {
                        return false;
                    }
                }
            }

            return this.ContainsAll(other);
        }

        /// <summary>
        /// Determines whether a <see cref="HashSet{T}"/> object is a proper superset of the specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns><c>true</c> if the <see cref="HashSet{T}"/> object is a proper superset of other; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual bool IsProperSupersetOf(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // the empty set isn't a proper superset of any set.
            if (this.size == 0)
            {
                return false;
            }

            if (other is SCG.ICollection<T> otherAsCollection)
            {
                // if other is the empty set then this is a superset
                if (otherAsCollection.Count == 0)
                    return true; // note that this has at least one element, based on above check

                // faster if other is a hashset with the same equality comparer
                if (AreEqualityComparersEqual(this, otherAsCollection))
                {
                    if (otherAsCollection.Count >= this.size)
                    {
                        return false;
                    }
                    // now perform element check
                    return ContainsAll(otherAsCollection);
                }
            }

            // couldn't fall out in the above cases; do it the long way
            this.CheckUniqueAndUnfoundElements(other, true, out int uniqueCount, out int unfoundCount);
            return uniqueCount < this.size && unfoundCount == 0;
        }

        /// <summary>
        /// Determines whether a <see cref="HashSet{T}"/> object is a proper subset of the specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns><c>true</c> if the <see cref="HashSet{T}"/> object is a proper subset of other; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual bool IsProperSubsetOf(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));


            if (other is SCG.ICollection<T> otherAsCollection)
            {
                // the empty set is a proper subset of anything but the empty set
                if (this.size == 0)
                    return otherAsCollection.Count > 0;

                // faster if other is a hashset (and we're using same equality comparer)
                if (AreEqualityComparersEqual(this, otherAsCollection))
                {
                    if (this.size >= otherAsCollection.Count)
                    {
                        return false;
                    }
                    // this has strictly less than number of items in other, so the following
                    // check suffices for proper subset.
                    return IsSubsetOfHashSetWithSameEC(otherAsCollection);
                }
            }

            this.CheckUniqueAndUnfoundElements(other, false, out int uniqueCount, out int unfoundCount);
            return uniqueCount == this.size && unfoundCount > 0;
        }

        /// <summary>
        /// Determines whether the current <see cref="HashSet{T}"/> object and a specified collection share common elements.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns><c>true</c> if the <see cref="HashSet{T}"/> object and other share at least one common element; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual bool Overlaps(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            if (this.size != 0)
            {
                foreach (var local in other)
                {
                    if (this.Contains(local))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the current <see cref="HashSet{T}"/> and the specified collection contain the same elements.
        /// </summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/>.</param>
        /// <returns><c>true</c> if the current <see cref="HashSet{T}"/> is equal to other; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="other"/> is <c>null</c>.</exception>
        public virtual bool SetEquals(SCG.IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // faster if other is a treeset and we're using same equality comparer
            if (other is SCG.ICollection<T> otherAsCollection)
            {
                if (AreEqualityComparersEqual(this, otherAsCollection))
                {
                    // attempt to return early: since both contain unique elements, if they have 
                    // different counts, then they can't be equal
                    if (this.size != otherAsCollection.Count)
                        return false;

                    // already confirmed that the sets have the same number of distinct elements, so if
                    // one is a superset of the other then they must be equal
                    return ContainsAll(otherAsCollection);
                }
                else
                {
                    // if this count is 0 but other contains at least one element, they can't be equal
                    if (this.size == 0 && otherAsCollection.Count > 0)
                        return false;
                }
            }

            this.CheckUniqueAndUnfoundElements(other, true, out int uniqueCount, out int unfoundCount);
            return uniqueCount == this.size && unfoundCount == 0;
        }

        private void CheckUniqueAndUnfoundElements(SCG.IEnumerable<T> other, bool returnIfUnfound, out int uniqueCount, out int unfoundCount)
        {
            // need special case in case this has no elements.
            if (this.size == 0)
            {
                int numElementsInOther = 0;
                foreach (T item in other)
                {
                    numElementsInOther++;
                    // break right away, all we want to know is whether other has 0 or 1 elements
                    break;
                }
                uniqueCount = 0;
                unfoundCount = numElementsInOther;
                return;
            }

            int originalLastIndex = this.size;
            var bitArray = new System.Collections.BitArray(originalLastIndex, false);

            // count of unique items in other found in this
            uniqueCount = 0;
            // count of items in other not found in this
            unfoundCount = 0;

            foreach (var item in other)
            {
                var index = IndexOf(item);
                if (index >= 0)
                {
                    if (!bitArray.Get(index))
                    {
                        // item hasn't been seen yet
                        bitArray.Set(index, true);
                        uniqueCount++;
                    }
                }
                else
                {
                    unfoundCount++;
                    if (returnIfUnfound)
                        break;
                }
            }
        }

        /// <summary>
        /// Checks if equality comparers are equal. This is used for algorithms that can
        /// speed up if it knows the other item has unique elements. I.e. if they're using 
        /// different equality comparers, then uniqueness assumption between sets break.
        /// </summary>
        private static bool AreEqualityComparersEqual(HashSet<T> set1, SCG.ICollection<T> set2)
        {
            if (set2 is HashSet<T> hashSet)
                return set1.EqualityComparer.Equals(hashSet.EqualityComparer);
            else if (set2 is TreeSet<T> treeSet)
                return set1.EqualityComparer.Equals(treeSet.EqualityComparer);
            else if (set2 is SCG.HashSet<T> scgHashSet)
                return set1.EqualityComparer.Equals(scgHashSet.Comparer);
            return false;
        }

        /// <summary>
        /// If other is a hashset that uses same equality comparer, intersect is much faster 
        /// because we can use other's Contains
        /// </summary>
        private void IntersectWithHashSetWithSameEC(SCG.ICollection<T> other)
        {
            foreach (var item in this)
            {
                if (!other.Contains(item))
                {
                    Remove(item);
                }
            }
        }

        private void IntersectWithEnumerable(SCG.IEnumerable<T> other)
        {
            // keep track of current last index; don't want to move past the end of our bit array
            // (could happen if another thread is modifying the collection)
            int originalLastIndex = this.size;
            var bitArray = new System.Collections.BitArray(originalLastIndex, false);

            foreach (var item in other)
            {
                int index = IndexOf(item);
                if (index >= 0)
                    bitArray.Set(index, true);
            }

            // if anything unmarked, remove it.
            for (int i = originalLastIndex - 1; i >= 0; i--)
            {
                if (!bitArray.Get(i))
                    RemoveAt(i);
            }
        }

        /// <summary>
        /// if other is a set, we can assume it doesn't have duplicate elements, so use this
        /// technique: if can't remove, then it wasn't present in this set, so add.
        /// 
        /// As with other methods, callers take care of ensuring that other is a hashset using the
        /// same equality comparer.
        /// </summary>
        private void SymmetricExceptWithUniqueHashSet(SCG.ICollection<T> other)
        {
            foreach (T item in other)
            {
                if (!Remove(item))
                {
                    Add(item);
                }
            }
        }

        /// <summary>
        /// Implementation Notes:
        /// If other is a hashset and is using same equality comparer, then checking subset is 
        /// faster. Simply check that each element in this is in other.
        /// 
        /// Note: if other doesn't use same equality comparer, then Contains check is invalid,
        /// which is why callers must take are of this.
        /// 
        /// If callers are concerned about whether this is a proper subset, they take care of that.
        ///
        /// </summary>
        private bool IsSubsetOfHashSetWithSameEC(SCG.ICollection<T> other)
        {

            foreach (T item in this)
            {
                if (!other.Contains(item))
                {
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region ISink<T> Members
        /// <summary>
        /// Report if this is a set collection.
        /// </summary>
        /// <value>Always false</value>
        public virtual bool AllowsDuplicates => false;

        /// <summary>
        /// By convention this is true for any collection with set semantics.
        /// </summary>
        /// <value>True if only one representative of a group of equal items 
        /// is kept in the collection together with the total count.</value>
        public virtual bool DuplicatesByCounting => true;

        /// <summary>
        /// Add an item to this set.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <returns>True if item was added (i.e. not found)</returns>
        public override bool Add(T item)
        {
            UpdateCheck();
            return !SearchOrAdd(ref item, true, false, true);
        }

        /// <summary>
        /// Add an item to this set.
        /// </summary>
        /// <param name="item">The item to add.</param>
        void SCG.ICollection<T>.Add(T item) => Add(item);

        /// <summary>
        /// Add the elements from another collection with a more specialized item type 
        /// to this collection. Since this
        /// collection has set semantics, only items not already in the collection
        /// will be added.
        /// </summary>
        /// <param name="items">The items to add</param>
        public virtual void AddAll(SCG.IEnumerable<T> items)
        {
            UpdateCheck();
            bool wasChanged = false;
            bool raiseAdded = (ActiveEvents & EventType.Added) != 0;
            CircularQueue<T>? wasAdded = raiseAdded ? new CircularQueue<T>() : null;
            foreach (T item in items)
            {
                T jtem = item;

                if (!SearchOrAdd(ref jtem, true, false, false))
                {
                    wasChanged = true;
                    if (raiseAdded)
                    {
                        wasAdded?.Enqueue(item);
                    }
                }
            }
            //TODO: implement a RaiseForAddAll() method
            if (raiseAdded & wasChanged)
            {
                if (wasAdded != null)
                {
                    foreach (T item in wasAdded)
                    {
                        RaiseItemsAdded(item, 1);
                    }
                }
            }

            if (((ActiveEvents & EventType.Changed) != 0 && wasChanged))
            {
                RaiseCollectionChanged();
            }
        }


        #endregion

        #region Diagnostics

        /// <summary>
        /// Test internal structure of data (invariants)
        /// </summary>
        /// <returns>True if pass</returns>
        public virtual bool Check()
        {
            int count = 0;
            bool retval = true;

            if (bitsc != 32 - bits)
            {
                Logger.Log(string.Format("bitsc != 32 - bits ({0}, {1})", bitsc, bits));
                retval = false;
            }
            if (indexmask != (1 << bits) - 1)
            {
                Logger.Log(string.Format("indexmask != (1 << bits) - 1 ({0}, {1})", indexmask, bits));
                retval = false;
            }
            if (table.Length != indexmask + 1)
            {
                Logger.Log(string.Format("table.Length != indexmask + 1 ({0}, {1})", table.Length, indexmask));
                retval = false;
            }
            if (bitsc != 32 - bits)
            {
                Logger.Log(string.Format("resizethreshhold != (int)(table.Length * fillfactor) ({0}, {1}, {2})", resizethreshhold, table.Length, fillfactor));
                retval = false;
            }

            for (int i = 0, s = table.Length; i < s; i++)
            {
                int level = 0;
                Bucket? b = table[i];
                while (b != null)
                {
                    if (i != Hv2i(b.hashval))
                    {
                        Logger.Log(string.Format("Bad cell item={0}, hashval={1}, index={2}, level={3}", b.item, b.hashval, i, level));
                        retval = false;
                    }

                    count++;
                    level++;
                    b = b.overflow;
                }
            }

            if (count != size)
            {
                Logger.Log(string.Format("size({0}) != count({1})", size, count));
                retval = false;
            }

            return retval;
        }


        /// <summary>
        /// Produce statistics on distribution of bucket sizes. Current implementation is incomplete.
        /// </summary>
        /// <returns>Histogram data.</returns>
        public ISortedDictionary<int, int> BucketCostDistribution()
        {
            TreeDictionary<int, int> res = new TreeDictionary<int, int>();
            for (int i = 0, s = table.Length; i < s; i++)
            {
                int count = 0;
                Bucket? b = table[i];

                while (b != null)
                {
                    count++;
                    b = b.overflow;
                }
                if (res.Contains(count))
                {
                    res[count]++;
                }
                else
                {
                    res[count] = 1;
                }
            }

            return res;
        }

        #endregion
    }
}
