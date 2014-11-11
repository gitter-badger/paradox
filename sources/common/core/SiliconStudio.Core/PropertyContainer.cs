﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using SiliconStudio.Core.Reflection;
using SiliconStudio.Core.Serialization.Serializers;

namespace SiliconStudio.Core
{
    /// <summary>
    /// Represents a container that can hold properties, lightweight to embed (lazy initialized).
    /// </summary>
    /// <remarks>
    /// Tag properties system purpose is to allow binding of properties that aren't logically supposed to be
    /// in a general class (probably because the property exists only in a higher level part of the engine).
    /// A typical example includes source mesh, collision data and various bouding volumes for a Geometry object:
    /// including them directly in the low-level Geometry class would be a terrible design decision !
    /// And the other well known solution, which consist of maintaining a Dictionary of object to properties
    /// isn't really nice either (especially with non-deterministic object destruction, it's hard to clean it up, would require lot of events).
    /// As a result, a specific system has been implemented.
    /// A class that could hold such tag properties should have an instance of <see cref="PropertyContainer"/> as a mutable field member.
    /// An cool feature of this system is that if a property doesn't exist, it could be generated during first access from a delegate or come from a default value.
    /// </remarks>
    [DataContract]
    [DataSerializer(typeof(PropertyContainer.Serializer))]
    public partial struct PropertyContainer : IEnumerable<KeyValuePair<PropertyKey, object>> //IDictionary<PropertyKey, object>
    {
        private static readonly Dictionary<Type, List<PropertyKey>> accessorProperties = new Dictionary<Type, List<PropertyKey>>();
        private Dictionary<PropertyKey, object> properties;
        private object owner;
        private static ReadOnlyCollection<PropertyKey> emptyKeys = new ReadOnlyCollection<PropertyKey>(new List<PropertyKey>());
        private static ReadOnlyCollection<object> emptyValues = new ReadOnlyCollection<object>(new List<object>());

        /// <summary>
        /// Property changed delegate.
        /// </summary>
        /// <param name="container">The property container.</param>
        /// <param name="key">The property key.</param>
        /// <param name="newValue">The property new value.</param>
        /// <param name="oldValue">The property old value.</param>
        public delegate void PropertyUpdatedDelegate(ref PropertyContainer propertyContainer, PropertyKey propertyKey, object newValue, object oldValue);

        /// <summary>
        /// Occurs when a property is modified.
        /// </summary>
        public event PropertyUpdatedDelegate PropertyUpdated;

        public PropertyContainer(object owner)
        {
            properties = null;
            PropertyUpdated = null;
            this.owner = owner;
        }

        public object Owner
        {
            get
            {
                return owner;
            }
            private set
            {
                owner = value;
            }
        }

        /// <summary>
        /// Gets the key-properties value pairs in this instance.
        /// </summary>
        public IEnumerator<KeyValuePair<PropertyKey, object>> GetEnumerator()
        {
            if (properties != null)
            {
                foreach (var property in properties)
                {
                    yield return new KeyValuePair<PropertyKey, object>(property.Key, property.Value);
                }
            }

            if (Owner != null)
            {
                var currentType = Owner.GetType();
                while (currentType != null)
                {
                    List<PropertyKey> typeAccessorProperties;
                    if (accessorProperties.TryGetValue(currentType, out typeAccessorProperties))
                    {
                        foreach (var accessorProperty in typeAccessorProperties)
                        {
                            yield return new KeyValuePair<PropertyKey, object>(accessorProperty, accessorProperty.AccessorMetadata.GetValue(ref this));
                        }
                    }

                    currentType = currentType.GetTypeInfo().BaseType;
                }
            }
        }

        public void Clear()
        {
            if (properties != null) properties.Clear();
        }

        /// <summary>
        /// Gets the number of properties stored in this container.
        /// </summary>
        /// <value>The count of properties.</value>
        public int Count
        {
            get
            {
                return properties != null ? properties.Count : 0;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Adds the specified key-value pair.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Add<T>(PropertyKey<T> key, T value)
        {
            SetObject(key, value, true);
        }

        /// <summary>
        /// Determines whether the specified instance contains this key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>
        ///   <c>true</c> if the specified instance contains this key; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsKey(PropertyKey key)
        {
            return properties != null && properties.ContainsKey(key);
        }

        public void Add(PropertyKey key, object value)
        {
            SetObject(key, value, true);
        }

        public bool Remove(PropertyKey propertyKey)
        {
            bool removed = false;

            if (PropertyUpdated != null || propertyKey.PropertyUpdateCallback != null)
            {
                object previousValue = Get(propertyKey);

                if (properties != null)
                    removed = properties.Remove(propertyKey);
                var tagValue = Get(propertyKey);

                if (!ArePropertyValuesEqual(propertyKey, tagValue, previousValue))
                {
                    if (propertyKey.PropertyUpdateCallback != null)
                        propertyKey.PropertyUpdateCallback(ref this, propertyKey, tagValue, previousValue);
                    if (PropertyUpdated != null)
                        PropertyUpdated(ref this, propertyKey, tagValue, previousValue);
                }
            }
            else
            {
                if (properties != null)
                    removed = properties.Remove(propertyKey);
            }

            return removed;
        }

        public object this[PropertyKey key]
        {
            get
            {
                return Get(key);
            }
            set
            {
                SetObject(key, value);
            }
        }

        public ICollection<PropertyKey> Keys
        {
            get
            {
                if (properties != null) return properties.Keys;
                return emptyKeys;
            }
        }

        public ICollection<object> Values
        {
            get
            {
                if (properties != null) return properties.Values;
                return emptyValues;
            }
        }

        /// <summary>
        /// Copies properties from this instance to a container.
        /// </summary>
        /// <param name="destination">The destination.</param>
        public void CopyTo(ref PropertyContainer destination)
        {
            foreach (var keyValuePair in this)
                destination.SetObject(keyValuePair.Key, keyValuePair.Value);
        }

        /// <summary>
        /// Gets the specified tag value.
        /// </summary>
        /// <param name="propertyKey">The tag property.</param>
        /// <returns>Value of the tag property</returns>
        public object Get(PropertyKey propertyKey)
        {
            return Get(propertyKey, false);
        }

        private object Get(PropertyKey propertyKey, bool forceNotToKeep)
        {
            // First, check if there is an accessor
            if (propertyKey.AccessorMetadata != null)
            {
                return propertyKey.AccessorMetadata.GetValue(ref this);
            }

            object value;

            // Get bound value, if any.
            if (properties != null && properties.TryGetValue(propertyKey, out value))
            {
                if (propertyKey.IsValueType)
                    value = ((ValueHolder)value).ObjectValue;
                return value;
            }

            if (propertyKey.DefaultValueMetadata != null)
            {
                // Get default value.
                object defaultValue = propertyKey.DefaultValueMetadata.GetDefaultValue(ref this);

                // Check if value should be kept.
                if (propertyKey.DefaultValueMetadata.KeepValue && !forceNotToKeep)
                {
                    // Register it.
                    SetObject(propertyKey, defaultValue);
                }
                return defaultValue;
            }

            return null;
        }

        /// <summary>
        /// Gets the specified tag value.
        /// </summary>
        /// <typeparam name="T">Type of the tag value</typeparam>
        /// <param name="propertyKey">The tag property.</param>
        /// <returns>Typed value of the tag property</returns>
        public T Get<T>(PropertyKey<T> propertyKey)
        {
            if (propertyKey.IsValueType)
            {
                // Fast path for value type
                // First, check if there is an accessor
                if (propertyKey.AccessorMetadata != null)
                {
                    // TODO: Not optimal, but not used so far
                    return (T)propertyKey.AccessorMetadata.GetValue(ref this);
                }

                object value;

                // Get bound value, if any.
                if (properties != null && properties.TryGetValue(propertyKey, out value))
                    return ((ValueHolder<T>)value).Value;

                if (propertyKey.DefaultValueMetadata != null)
                {
                    // Get default value.
                    T defaultValue = ((DefaultValueMetadata<T>)propertyKey.DefaultValueMetadata).GetDefaultValueT(ref this);

                    // Check if value should be kept.
                    if (propertyKey.DefaultValueMetadata.KeepValue)
                    {
                        // Register it.
                        Set(propertyKey, defaultValue);
                    }

                    return defaultValue;
                }

                return default(T);
            }

            var result = Get((PropertyKey)propertyKey, false);
            return result != null ? (T)result : default(T);
        }

        /// <summary>
        /// Tries to get a tag value.
        /// </summary>
        /// <typeparam name="T">Type of the tag value</typeparam>
        /// <param name="propertyKey">The tag property.</param>
        /// <param name="value">The value or default vaue if not found</param>
        /// <returns>Returns <c>true</c> if the was found; <c>false</c> otherwise</returns>
        public bool TryGetValue(PropertyKey propertyKey, out object value)
        {
            // Implem to avoid boxing/unboxing when using object as output value
            if (ContainsKey(propertyKey))
            {
                var result = Get(propertyKey);
                value = result;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Tries to get a tag value.
        /// </summary>
        /// <typeparam name="T">Type of the tag value</typeparam>
        /// <param name="propertyKey">The tag property.</param>
        /// <param name="value">The value or default vaue if not found</param>
        /// <returns>Returns <c>true</c> if the was found; <c>false</c> otherwise</returns>
        public bool TryGetValue<T>(PropertyKey<T> propertyKey, out T value)
        {
            if (ContainsKey(propertyKey))
            {
                value = Get<T>(propertyKey);
                return true;
            }
            value = default(T);
            return false;
        }

        /// <summary>
        /// Sets the specified tag value.
        /// </summary>
        /// <typeparam name="T">Type of the tag value</typeparam>
        /// <param name="propertyKey">The tag property.</param>
        /// <param name="tagValue">The tag value.</param>
        public void Set<T>(PropertyKey<T> propertyKey, T tagValue)
        {
            if (propertyKey.IsValueType)
            {
                ValueHolder<T> valueHolder = null;
                T oldValue;

                // Fast path for value types
                // Fast path for value type
                // First, check if there is an accessor
                if (propertyKey.AccessorMetadata != null)
                {
                    // TODO: Not optimal, but not used so far
                    oldValue = (T)propertyKey.AccessorMetadata.GetValue(ref this);
                }
                else
                {
                    object value;

                    // Get bound value, if any.
                    if (properties != null && properties.TryGetValue(propertyKey, out value))
                    {
                        valueHolder = (ValueHolder<T>)value;
                        oldValue = valueHolder.Value;
                    }
                    else if (propertyKey.DefaultValueMetadata != null)
                    {
                        // Get default value.
                        oldValue = propertyKey.DefaultValueMetadataT.GetDefaultValueT(ref this);
                    }
                    else
                    {
                        oldValue = default(T);
                    }
                }

                // Allow to validate the metadata before storing it.
                if (propertyKey.ValidateValueMetadata != null)
                {
                    // TODO: Use typed validate?
                    propertyKey.ValidateValueMetadataT.ValidateValueCallback(ref tagValue);
                }

                // First, check if there is an accessor
                if (propertyKey.AccessorMetadata != null)
                {
                    // TODO: Not optimal, but not used so far
                    propertyKey.AccessorMetadata.SetValue(ref this, tagValue);
                    return;
                }

                if (properties == null)
                    properties = new Dictionary<PropertyKey, object>();

                if (valueHolder != null)
                    valueHolder.Value = tagValue;
                else
                    valueHolder = new ValueHolder<T>(tagValue);

                if (PropertyUpdated != null || propertyKey.PropertyUpdateCallback != null)
                {
                    object previousValue = GetNonRecursive(propertyKey);

                    properties[propertyKey] = valueHolder;

                    if (!ArePropertyValuesEqual(propertyKey, tagValue, previousValue))
                    {
                        if (PropertyUpdated != null)
                            PropertyUpdated(ref this, propertyKey, tagValue, previousValue);
                        if (propertyKey.PropertyUpdateCallback != null)
                            propertyKey.PropertyUpdateCallback(ref this, propertyKey, tagValue, previousValue);
                    }
                }
                else
                {
                    properties[propertyKey] = valueHolder;
                }

                if (propertyKey.ObjectInvalidationMetadata != null)
                {
                    propertyKey.ObjectInvalidationMetadataT.Invalidate(Owner, propertyKey, ref oldValue);
                }

                return;
            }

            SetObject((PropertyKey)propertyKey, tagValue, false);
        }

        /// <summary>
        /// Sets the specified tag value.
        /// </summary>
        /// <param name="propertyKey">The tag property.</param>
        /// <param name="tagValue">The tag value.</param>
        public void SetObject(PropertyKey propertyKey, object tagValue)
        {
            SetObject(propertyKey, tagValue, false);
        }

        private void SetObject(PropertyKey propertyKey, object tagValue, bool tryToAdd = false)
        {
            var oldValue = Get(propertyKey, true);

            // Allow to validate the metadata before storing it.
            if (propertyKey.ValidateValueMetadata != null)
            {
                propertyKey.ValidateValueMetadata.Validate(ref tagValue);
            }

            // First, check if there is an accessor
            if (propertyKey.AccessorMetadata != null)
            {
                propertyKey.AccessorMetadata.SetValue(ref this, tagValue);
                return;
            }

            if (properties == null)
                properties = new Dictionary<PropertyKey, object>();

            var valueToSet = propertyKey.IsValueType ? propertyKey.CreateValueHolder(tagValue) : tagValue;

            if (PropertyUpdated != null || propertyKey.PropertyUpdateCallback != null)
            {
                object previousValue = GetNonRecursive(propertyKey);

                if (tryToAdd)
                    properties.Add(propertyKey, valueToSet);
                else
                    properties[propertyKey] = valueToSet;

                if (!ArePropertyValuesEqual(propertyKey, tagValue, previousValue))
                {
                    if (PropertyUpdated != null)
                        PropertyUpdated(ref this, propertyKey, tagValue, previousValue);
                    if (propertyKey.PropertyUpdateCallback != null)
                        propertyKey.PropertyUpdateCallback(ref this, propertyKey, tagValue, previousValue);
                }
            }
            else
            {
                if (tryToAdd)
                    properties.Add(propertyKey, valueToSet);
                else
                    properties[propertyKey] = valueToSet;
            }

            if (propertyKey.ObjectInvalidationMetadata != null)
            {
                propertyKey.ObjectInvalidationMetadata.Invalidate(Owner, propertyKey, oldValue);
            }
        }

        public static void AddAccessorProperty(Type type, PropertyKey propertyKey)
        {
            if (!type.GetTypeInfo().IsClass)
                throw new ArgumentException("Class type is expected.", "type");

            if (propertyKey.AccessorMetadata == null)
                throw new ArgumentException("Given PropertyKey doesn't have accessor metadata.", "propertyKey");

            List<PropertyKey> typeAccessorProperties;
            if (!accessorProperties.TryGetValue(type, out typeAccessorProperties))
                accessorProperties.Add(type, typeAccessorProperties = new List<PropertyKey>());

            typeAccessorProperties.Add(propertyKey);
        }

        internal void RaisePropertyContainerUpdated(PropertyKey propertyKey, object newValue, object oldValue)
        {
            if (PropertyUpdated != null)
                PropertyUpdated(ref this, propertyKey, newValue, oldValue);
        }

        private object GetNonRecursive(PropertyKey propertyKey)
        {
            object value;

            // Get bound value, if any.
            if (properties != null && properties.TryGetValue(propertyKey, out value))
            {
                if (propertyKey.IsValueType)
                    return ((ValueHolder)value).ObjectValue;
                return value;
            }

            if (propertyKey.DefaultValueMetadata != null)
            {
                // Get default value.
                return propertyKey.DefaultValueMetadata.GetDefaultValue(ref this);
            }

            return null;
        }

        private static bool ArePropertyValuesEqual(PropertyKey propertyKey, object propertyValue1, object propertyValue2)
        {
            var propertyType = propertyKey.PropertyType;

            if (!propertyType.GetTypeInfo().IsValueType && propertyType != typeof(string))
            {
                return object.ReferenceEquals(propertyValue1, propertyValue2);
            }

            return object.Equals(propertyValue1, propertyValue2);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /*void ICollection<KeyValuePair<PropertyKey, object>>.Add(KeyValuePair<PropertyKey, object> item)
        {
            Add(item.Key, item.Value);
        }

        bool ICollection<KeyValuePair<PropertyKey, object>>.Contains(KeyValuePair<PropertyKey, object> item)
        {
            object value;
            if (TryGetValue(item.Key, out value))
            {
                return Equals(value, item.Value);
            }
            return false;
        }

        void ICollection<KeyValuePair<PropertyKey, object>>.CopyTo(KeyValuePair<PropertyKey, object>[] array, int arrayIndex)
        {
            if (properties != null)
            {
                ((IDictionary<PropertyKey, object>)properties).CopyTo(array, arrayIndex);
            }
        }

        bool ICollection<KeyValuePair<PropertyKey, object>>.Remove(KeyValuePair<PropertyKey, object> item)
        {
            object value;
            if (TryGetValue(item.Key, out value))
            {
                if (Equals(value, item.Value))
                {
                    Remove(item.Key);
                }
            }
            return false;
        }
         */

        internal abstract class ValueHolder
        {
            public abstract object ObjectValue { get; }
        }

        internal class ValueHolder<T> : ValueHolder
        {
            public T Value;

            public ValueHolder(T value)
            {
                Value = value;
            }

            public override object ObjectValue
            {
                get { return Value; }
            }
        }
    }
}