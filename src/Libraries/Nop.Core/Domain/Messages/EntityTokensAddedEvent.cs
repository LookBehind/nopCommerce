using System.Collections.Generic;

namespace Nop.Core.Domain.Messages
{
    /// <summary>
    /// A container for tokens that are added.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <typeparam name="U"></typeparam>
    public class EntityTokensAddedEvent<T, U> where T : BaseEntity
    {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="entity">Entity</param>
        /// <param name="tokens">Tokens</param>
        public EntityTokensAddedEvent(T entity, IList<U> tokens)
        {
            Entity = entity;
            Tokens = tokens;
        }

        /// <summary>
        /// Entity
        /// </summary>
        public T Entity { get; }

        /// <summary>
        /// Tokens
        /// </summary>
        public IList<U> Tokens { get; }
    }
    
    /// <summary>
    /// A container for tokens that are added.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <typeparam name="R"></typeparam>
    /// <typeparam name="U"></typeparam>
    public class EntityTokensAddedEvent<T, R, U> where T : BaseEntity
    {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="entity">Entity</param>
        /// <param name="attachedParam">Attached parameter</param>
        /// <param name="tokens">Tokens</param>
        public EntityTokensAddedEvent(T entity, R attachedParam, IList<U> tokens)
        {
            Entity = entity;
            AttachedParam = attachedParam;
            Tokens = tokens;
        }

        /// <summary>
        /// Entity
        /// </summary>
        public T Entity { get; }
        
        /// <summary>
        /// Entity
        /// </summary>
        public R AttachedParam { get; }

        /// <summary>
        /// Tokens
        /// </summary>
        public IList<U> Tokens { get; }
    }
}