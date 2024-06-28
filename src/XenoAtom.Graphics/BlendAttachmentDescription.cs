using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A <see cref="Pipeline"/> component describing the blend behavior for an individual color attachment.
    /// </summary>
    public record struct BlendAttachmentDescription
    {
        /// <summary>
        /// Controls whether blending is enabled for the color attachment.
        /// </summary>
        public bool BlendEnabled;
        /// <summary>
        /// Controls which components of the color will be written to the framebuffer. The default is <see cref="ColorWriteMask.All"/>.
        /// </summary>
        public ColorWriteMask ColorWriteMask;
        /// <summary>
        /// Controls the source color's influence on the blend result.
        /// </summary>
        public BlendFactor SourceColorFactor;
        /// <summary>
        /// Controls the destination color's influence on the blend result.
        /// </summary>
        public BlendFactor DestinationColorFactor;
        /// <summary>
        /// Controls the function used to combine the source and destination color factors.
        /// </summary>
        public BlendFunction ColorFunction;
        /// <summary>
        /// Controls the source alpha's influence on the blend result.
        /// </summary>
        public BlendFactor SourceAlphaFactor;
        /// <summary>
        /// Controls the destination alpha's influence on the blend result.
        /// </summary>
        public BlendFactor DestinationAlphaFactor;
        /// <summary>
        /// Controls the function used to combine the source and destination alpha factors.
        /// </summary>
        public BlendFunction AlphaFunction;


        public BlendAttachmentDescription()
        {
            ColorWriteMask = ColorWriteMask.All;
        }


        /// <summary>
        /// Constructs a new <see cref="BlendAttachmentDescription"/>.
        /// </summary>
        /// <param name="blendEnabled">Controls whether blending is enabled for the color attachment.</param>
        /// <param name="sourceColorFactor">Controls the source color's influence on the blend result.</param>
        /// <param name="destinationColorFactor">Controls the destination color's influence on the blend result.</param>
        /// <param name="colorFunction">Controls the function used to combine the source and destination color factors.</param>
        /// <param name="sourceAlphaFactor">Controls the source alpha's influence on the blend result.</param>
        /// <param name="destinationAlphaFactor">Controls the destination alpha's influence on the blend result.</param>
        /// <param name="alphaFunction">Controls the function used to combine the source and destination alpha factors.</param>
        public BlendAttachmentDescription(
            bool blendEnabled,
            BlendFactor sourceColorFactor,
            BlendFactor destinationColorFactor,
            BlendFunction colorFunction,
            BlendFactor sourceAlphaFactor,
            BlendFactor destinationAlphaFactor,
            BlendFunction alphaFunction)
        {
            BlendEnabled = blendEnabled;
            SourceColorFactor = sourceColorFactor;
            DestinationColorFactor = destinationColorFactor;
            ColorFunction = colorFunction;
            SourceAlphaFactor = sourceAlphaFactor;
            DestinationAlphaFactor = destinationAlphaFactor;
            AlphaFunction = alphaFunction;
            ColorWriteMask = ColorWriteMask.All;
        }

        /// <summary>
        /// Constructs a new <see cref="BlendAttachmentDescription"/>.
        /// </summary>
        /// <param name="blendEnabled">Controls whether blending is enabled for the color attachment.</param>
        /// <param name="colorWriteMask">Controls which components of the color will be written to the framebuffer.</param>
        /// <param name="sourceColorFactor">Controls the source color's influence on the blend result.</param>
        /// <param name="destinationColorFactor">Controls the destination color's influence on the blend result.</param>
        /// <param name="colorFunction">Controls the function used to combine the source and destination color factors.</param>
        /// <param name="sourceAlphaFactor">Controls the source alpha's influence on the blend result.</param>
        /// <param name="destinationAlphaFactor">Controls the destination alpha's influence on the blend result.</param>
        /// <param name="alphaFunction">Controls the function used to combine the source and destination alpha factors.</param>
        public BlendAttachmentDescription(
            bool blendEnabled,
            ColorWriteMask colorWriteMask,
            BlendFactor sourceColorFactor,
            BlendFactor destinationColorFactor,
            BlendFunction colorFunction,
            BlendFactor sourceAlphaFactor,
            BlendFactor destinationAlphaFactor,
            BlendFunction alphaFunction)
        {
            BlendEnabled = blendEnabled;
            ColorWriteMask = colorWriteMask;
            SourceColorFactor = sourceColorFactor;
            DestinationColorFactor = destinationColorFactor;
            ColorFunction = colorFunction;
            SourceAlphaFactor = sourceAlphaFactor;
            DestinationAlphaFactor = destinationAlphaFactor;
            AlphaFunction = alphaFunction;
        }

        /// <summary>
        /// Describes a blend attachment state in which the source completely overrides the destination.
        /// Settings:
        ///     BlendEnabled = true
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.One
        ///     DestinationColorFactor = BlendFactor.Zero
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.One
        ///     DestinationAlphaFactor = BlendFactor.Zero
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendAttachmentDescription OverrideBlend = new()
        {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.One,
            DestinationColorFactor = BlendFactor.Zero,
            ColorFunction = BlendFunction.Add,
            SourceAlphaFactor = BlendFactor.One,
            DestinationAlphaFactor = BlendFactor.Zero,
            AlphaFunction = BlendFunction.Add,
        };

        /// <summary>
        /// Describes a blend attachment state in which the source and destination are blended in an inverse relationship.
        /// Settings:
        ///     BlendEnabled = true
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.SourceAlpha
        ///     DestinationColorFactor = BlendFactor.InverseSourceAlpha
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.SourceAlpha
        ///     DestinationAlphaFactor = BlendFactor.InverseSourceAlpha
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendAttachmentDescription AlphaBlend = new()
        {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.SourceAlpha,
            DestinationColorFactor = BlendFactor.InverseSourceAlpha,
            ColorFunction = BlendFunction.Add,
            SourceAlphaFactor = BlendFactor.SourceAlpha,
            DestinationAlphaFactor = BlendFactor.InverseSourceAlpha,
            AlphaFunction = BlendFunction.Add,
        };

        /// <summary>
        /// Describes a blend attachment state in which the source is added to the destination based on its alpha channel.
        /// Settings:
        ///     BlendEnabled = true
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.SourceAlpha
        ///     DestinationColorFactor = BlendFactor.One
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.SourceAlpha
        ///     DestinationAlphaFactor = BlendFactor.One
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendAttachmentDescription AdditiveBlend = new()
        {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.SourceAlpha,
            DestinationColorFactor = BlendFactor.One,
            ColorFunction = BlendFunction.Add,
            SourceAlphaFactor = BlendFactor.SourceAlpha,
            DestinationAlphaFactor = BlendFactor.One,
            AlphaFunction = BlendFunction.Add,
        };

        /// <summary>
        /// Describes a blend attachment state in which blending is disabled.
        /// Settings:
        ///     BlendEnabled = false
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.One
        ///     DestinationColorFactor = BlendFactor.Zero
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.One
        ///     DestinationAlphaFactor = BlendFactor.Zero
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendAttachmentDescription Disabled = new()
        {
            BlendEnabled = false,
            SourceColorFactor = BlendFactor.One,
            DestinationColorFactor = BlendFactor.Zero,
            ColorFunction = BlendFunction.Add,
            SourceAlphaFactor = BlendFactor.One,
            DestinationAlphaFactor = BlendFactor.Zero,
            AlphaFunction = BlendFunction.Add,
        };
    }
}
