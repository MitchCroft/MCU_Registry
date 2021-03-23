using System;

namespace MCU.Registry {
    /// <summary>
    /// Mark a function with a class designated as a <see cref="MCUAdapterAttributeAttribute"/> that the 
    /// associated method can be called dynamically
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class MCUAdapterMethodAttribute : Attribute {
        /*----------Properties----------*/
        //PUBLIC
    
        /// <summary>
        /// The name that can be used to raise the associated function
        /// </summary>
        public string FunctionName { get; private set; }

        /*----------Functions----------*/
        //PUBLIC

        /// <summary>
        /// Initialise this object with it's base values
        /// </summary>
        /// <param name="functionName">The name that can be used to raise the associated function</param>
        public MCUAdapterMethodAttribute(string functionName = "") {
            FunctionName = (string.IsNullOrWhiteSpace(functionName) ? string.Empty : functionName.Trim());
        }
    }
}