# XenoAtom.Graphics [![ci](https://github.com/XenoAtom/XenoAtom.Graphics/actions/workflows/ci.yml/badge.svg)](https://github.com/XenoAtom/XenoAtom.Graphics/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/XenoAtom.Graphics.svg)](https://www.nuget.org/packages/XenoAtom.Graphics/)

<img align="right" width="256px" height="256px" src="https://raw.githubusercontent.com/XenoAtom/XenoAtom.Graphics/main/img/XenoAtom.Graphics.png">

**XenoAtom.Graphics** is a low-level graphics library for .NET powered by Vulkan.

It is a fork of the excellent [Veldrid](https://github.com/veldrid/veldrid) library, updated to use [XenoAtom.Interop.vulkan](https://github.com/XenoAtom/XenoAtom.Interop/tree/main/src/vulkan) bindings and with a focus on Vulkan only. It will serve as a modern Graphics GPU API for experiementing within the XenoAtom project.

> **Note**: This library is still in early development and not yet ready for production use.
>
> Some changes are expected to be made to the API and the implementation. See issue [#1](https://github.com/XenoAtom/XenoAtom.Graphics/issues/1)

## ✨ Features 

- TBD

## 📃 User Guide

For more details on how to use XenoAtom.Graphics, please visit the [user guide](https://github.com/XenoAtom/XenoAtom.Graphics/blob/main/doc/readme.md).

## 🏗️ Build

You need to install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Then from the root folder:

```console
$ dotnet build src -c Release
```

## 🪪 License

This software is released under the [BSD-2-Clause license](https://opensource.org/licenses/BSD-2-Clause).

The license also integrate the original MIT license from [Veldrid](https://github.com/veldrid/veldrid/blob/master/LICENSE).

## 🤗 Authors

Alexandre Mutel aka [xoofx](https://xoofx.github.io).

[Eric Mellino](https://github.com/mellinoe) for the original Veldrid code.
