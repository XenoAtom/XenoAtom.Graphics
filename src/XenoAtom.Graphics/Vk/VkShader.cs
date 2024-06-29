using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkShader : Shader
    {
        private new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));
        private readonly VkShaderModule _shaderModule;

        public VkShaderModule ShaderModule => _shaderModule;

        public VkShader(VkGraphicsDevice gd, in ShaderDescription description)
            : base(gd, description.Stage, description.EntryPoint)
        {
            VkShaderModuleCreateInfo shaderModuleCI = new VkShaderModuleCreateInfo();
            fixed (byte* codePtr = description.ShaderBytes)
            {
                shaderModuleCI.codeSize = (UIntPtr)description.ShaderBytes.Length;
                shaderModuleCI.pCode = (uint*)codePtr;
                VkResult result = vkCreateShaderModule(gd.VkDevice, shaderModuleCI, null, out _shaderModule);
                CheckResult(result);
            }
        }

        internal override void Destroy()
        {
            vkDestroyShaderModule(Device, ShaderModule, null);
        }
    }
}
