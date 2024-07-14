// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics.Vk;

[Flags]
internal enum VkDeviceMemoryAllocationCreateFlags
{
    None = 0,

    /// <summary>
    ///     Set this flag if the allocation should have its own memory block.
    /// Use it for special, big resources, like fullscreen images used as attachments.
    /// If you use this flag while creating a buffer or an image, `VkMemoryDedicatedAllocateInfo`
    /// structure is applied if possible.
    /// </summary>
    Dedicated = 1 << 0,

    /// <summary>
    /// Set this flag to use a memory that will be persistently mapped and retrieve pointer to it.
    ///
    /// Pointer to mapped memory will be returned through VmaAllocationInfo::pMappedData.
    ///
    /// It is valid to use this flag for allocation made from memory type that is not
    /// `HOST_VISIBLE`. This flag is then ignored and memory is not mapped. This is
    /// useful if you need an allocation that is efficient to use on GPU
    /// (`DEVICE_LOCAL`) and still want to map it directly if possible on platforms that
    /// support it (e.g. Intel GPU).
    /// </summary>
    Mapped = 1 << 1,

    /// <summary>
    /// Requests possibility to map the allocation (using vmaMapMemory() or #VMA_ALLOCATION_CREATE_MAPPED_BIT).
    ///
    /// - If you use #VMA_MEMORY_USAGE_AUTO or other `VMA_MEMORY_USAGE_AUTO*` value,
    ///   you must use this flag to be able to map the allocation. Otherwise, mapping is incorrect.
    /// - If you use other value of #VmaMemoryUsage, this flag is ignored and mapping is always possible in memory types that are `HOST_VISIBLE`.
    ///   This includes allocations created in \ref custom_memory_pools.
    ///
    /// Declares that mapped memory will only be written sequentially, e.g. using `memcpy()` or a loop writing number-by-number,
    /// never read or accessed randomly, so a memory type can be selected that is uncached and write-combined.
    ///
    /// Violating this declaration may work correctly, but will likely be very slow.
    /// Watch out for implicit reads introduced by doing e.g. `pMappedData[i] += x;`
    /// Better prepare your data in a local variable and `memcpy()` it to the mapped pointer all at once.
    /// </summary>
    MappeableForSequentialWrite = 1 << 2,

    /// <summary>
    /// Requests possibility to map the allocation (using vmaMapMemory() or #VMA_ALLOCATION_CREATE_MAPPED_BIT).
    /// - If you use #VMA_MEMORY_USAGE_AUTO or other `VMA_MEMORY_USAGE_AUTO*` value,
    /// you must use this flag to be able to map the allocation. Otherwise, mapping is incorrect.
    /// - If you use other value of #VmaMemoryUsage, this flag is ignored and mapping is always possible in memory types that are `HOST_VISIBLE`.
    /// This includes allocations created in \ref custom_memory_pools.
    /// Declares that mapped memory can be read, written, and accessed in random order,
    /// so a `HOST_CACHED` memory type is preferred.
    /// </summary>
    MappeableForRandomAccess = 1 << 3,

    /// <summary>
    /// Specified when the image/buffer requires VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT.
    /// </summary>
    RequiredTransfer = 1 << 4,

    /// <summary>
    /// Together with #VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT or #VMA_ALLOCATION_CREATE_HOST_ACCESS_RANDOM_BIT,
    /// it says that despite request for host access, a not-`HOST_VISIBLE` memory type can be selected
    /// if it may improve performance.
    /// By using this flag, you declare that you will check if the allocation ended up in a `HOST_VISIBLE` memory type
    /// (e.g. using vmaGetAllocationMemoryProperties()) and if not, you will create some "staging" buffer and
    /// issue an explicit transfer to write/read your data.
    /// To prepare for this possibility, don't forget to add appropriate flags like
    /// `VK_BUFFER_USAGE_TRANSFER_DST_BIT`, `VK_BUFFER_USAGE_TRANSFER_SRC_BIT` to the parameters of created buffer or image.
    /// </summary>
    AllowTransfer = 1 << 5,
}