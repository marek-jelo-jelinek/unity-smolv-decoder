using System;

namespace Tesearis.SmolVDecoder
{
    /// <summary>Decode-only C# port of the SMOL-V -> SPIR-V decoder from aras-p/smol-v
    /// (https://github.com/aras-p/smol-v, MIT/public domain, authored by Aras Pranckevicius). Only the decode path
    /// is ported - this package never needs to produce SMOL-V, only read it back.
    /// <para/>Unity's <c>UnityEditor.Rendering.ShaderData.Pass.CompileVariant</c> does not return raw SPIR-V
    /// for Vulkan-platform variants - it returns Unity's own compiled-pass blob (Unity's Library/ shader cache
    /// stores Vulkan bytecode this way to save space). That blob concatenates one SMOL-V-compressed SPIR-V module
    /// per shader stage (vertex, fragment, ...) regardless of which single stage was requested from
    /// <c>CompileVariant</c>, which is why <see cref="TryDecodeStages"/> below has to scan for both
    /// modules.</summary>
    public static class SmolV
    {
        private const uint SmolHeaderMagic = 0x534D4F4C; // "SMOL"
        private const uint SpirvHeaderMagic = 0x07230203;

        // SpvOp values referenced by name in the decode logic below (from the SPIR-V spec / smolv.cpp's SpvOp enum).
        private const int OpNop = 0;
        private const int OpUndef = 1;
        private const int OpSourceContinued = 2;
        private const int OpSource = 3;
        private const int OpSourceExtension = 4;
        private const int OpString = 7;
        private const int OpLine = 8;
        private const int OpUnused9 = 9; // gap in the SPIR-V spec's opcode numbering - SMOL-V swaps it with OpVariable since it's rare
        private const int OpExtension = 10;
        private const int OpExtInstImport = 11;
        private const int OpVectorShuffleCompact = 13; // not a real SPIR-V op - SMOL-V's compact shuffle encoding
        private const int OpMemoryModel = 14;
        private const int OpEntryPoint = 15;
        private const int OpTypePointer = 32;
        private const int OpVariable = 59;
        private const int OpLoad = 61;
        private const int OpStore = 62;
        private const int OpAccessChain = 65;
        private const int OpDecorate = 71;
        private const int OpMemberDecorate = 72;
        private const int OpVectorShuffle = 79;
        private const int OpFNegate = 127;
        private const int OpFAdd = 129;
        private const int OpFMul = 133;
        private const int OpLabel = 248;
        private const int OpModuleProcessed = 330;
        private const int OpGroupNonUniformQuadSwap = 366;

        // SPIR-V's own OpEntryPoint Execution Model operand values (the ones commonly needed when decoding
        // Unity's Vulkan shader cache) - see the SPIR-V spec's "Execution Model" enumerant.
        private const uint ExecutionModelVertex = 0;
        private const uint ExecutionModelFragment = 4;

        private readonly struct OpInfo
        {
            public readonly byte HasResult;
            public readonly byte HasType;
            public readonly byte DeltaFromResult;
            public readonly byte VarRest;

            public OpInfo(byte hasResult, byte hasType, byte deltaFromResult, byte varRest)
            {
                HasResult = hasResult;
                HasType = hasType;
                DeltaFromResult = deltaFromResult;
                VarRest = varRest;
            }
        }

        // Generated verbatim from kSpirvOpData in aras-p/smol-v's source/smolv.cpp (367 entries, one per
        // known SpvOp up to SpvOpGroupNonUniformQuadSwap). Columns: hasResult, hasType, deltaFromResult, varRest.
        private static readonly OpInfo[] OpTable =
        {
            new(0, 0, 0, 0), // Nop
            new(1, 1, 0, 0), // Undef
            new(0, 0, 0, 0), // SourceContinued
            new(0, 0, 0, 1), // Source
            new(0, 0, 0, 0), // SourceExtension
            new(0, 0, 0, 0), // Name
            new(0, 0, 0, 0), // MemberName
            new(0, 0, 0, 0), // String
            new(0, 0, 0, 1), // Line
            new(1, 1, 0, 0), // #9
            new(0, 0, 0, 0), // Extension
            new(1, 0, 0, 0), // ExtInstImport
            new(1, 1, 0, 1), // ExtInst
            new(1, 1, 2, 1), // VectorShuffleCompact - new in SMOLV
            new(0, 0, 0, 1), // MemoryModel
            new(0, 0, 0, 1), // EntryPoint
            new(0, 0, 0, 1), // ExecutionMode
            new(0, 0, 0, 1), // Capability
            new(1, 1, 0, 0), // #18
            new(1, 0, 0, 1), // TypeVoid
            new(1, 0, 0, 1), // TypeBool
            new(1, 0, 0, 1), // TypeInt
            new(1, 0, 0, 1), // TypeFloat
            new(1, 0, 0, 1), // TypeVector
            new(1, 0, 0, 1), // TypeMatrix
            new(1, 0, 0, 1), // TypeImage
            new(1, 0, 0, 1), // TypeSampler
            new(1, 0, 0, 1), // TypeSampledImage
            new(1, 0, 0, 1), // TypeArray
            new(1, 0, 0, 1), // TypeRuntimeArray
            new(1, 0, 0, 1), // TypeStruct
            new(1, 0, 0, 1), // TypeOpaque
            new(1, 0, 0, 1), // TypePointer
            new(1, 0, 0, 1), // TypeFunction
            new(1, 0, 0, 1), // TypeEvent
            new(1, 0, 0, 1), // TypeDeviceEvent
            new(1, 0, 0, 1), // TypeReserveId
            new(1, 0, 0, 1), // TypeQueue
            new(1, 0, 0, 1), // TypePipe
            new(0, 0, 0, 1), // TypeForwardPointer
            new(1, 1, 0, 0), // #40
            new(1, 1, 0, 0), // ConstantTrue
            new(1, 1, 0, 0), // ConstantFalse
            new(1, 1, 0, 0), // Constant
            new(1, 1, 9, 0), // ConstantComposite
            new(1, 1, 0, 1), // ConstantSampler
            new(1, 1, 0, 0), // ConstantNull
            new(1, 1, 0, 0), // #47
            new(1, 1, 0, 0), // SpecConstantTrue
            new(1, 1, 0, 0), // SpecConstantFalse
            new(1, 1, 0, 0), // SpecConstant
            new(1, 1, 9, 0), // SpecConstantComposite
            new(1, 1, 0, 0), // SpecConstantOp
            new(1, 1, 0, 0), // #53
            new(1, 1, 0, 1), // Function
            new(1, 1, 0, 0), // FunctionParameter
            new(0, 0, 0, 0), // FunctionEnd
            new(1, 1, 9, 0), // FunctionCall
            new(1, 1, 0, 0), // #58
            new(1, 1, 0, 1), // Variable
            new(1, 1, 0, 0), // ImageTexelPointer
            new(1, 1, 1, 1), // Load
            new(0, 0, 2, 1), // Store
            new(0, 0, 0, 0), // CopyMemory
            new(0, 0, 0, 0), // CopyMemorySized
            new(1, 1, 0, 1), // AccessChain
            new(1, 1, 0, 0), // InBoundsAccessChain
            new(1, 1, 0, 0), // PtrAccessChain
            new(1, 1, 0, 0), // ArrayLength
            new(1, 1, 0, 0), // GenericPtrMemSemantics
            new(1, 1, 0, 0), // InBoundsPtrAccessChain
            new(0, 0, 0, 1), // Decorate
            new(0, 0, 0, 1), // MemberDecorate
            new(1, 0, 0, 0), // DecorationGroup
            new(0, 0, 0, 0), // GroupDecorate
            new(0, 0, 0, 0), // GroupMemberDecorate
            new(1, 1, 0, 0), // #76
            new(1, 1, 1, 1), // VectorExtractDynamic
            new(1, 1, 2, 1), // VectorInsertDynamic
            new(1, 1, 2, 1), // VectorShuffle
            new(1, 1, 9, 0), // CompositeConstruct
            new(1, 1, 1, 1), // CompositeExtract
            new(1, 1, 2, 1), // CompositeInsert
            new(1, 1, 1, 0), // CopyObject
            new(1, 1, 0, 0), // Transpose
            new(1, 1, 0, 0), // #85
            new(1, 1, 0, 0), // SampledImage
            new(1, 1, 2, 1), // ImageSampleImplicitLod
            new(1, 1, 2, 1), // ImageSampleExplicitLod
            new(1, 1, 3, 1), // ImageSampleDrefImplicitLod
            new(1, 1, 3, 1), // ImageSampleDrefExplicitLod
            new(1, 1, 2, 1), // ImageSampleProjImplicitLod
            new(1, 1, 2, 1), // ImageSampleProjExplicitLod
            new(1, 1, 3, 1), // ImageSampleProjDrefImplicitLod
            new(1, 1, 3, 1), // ImageSampleProjDrefExplicitLod
            new(1, 1, 2, 1), // ImageFetch
            new(1, 1, 3, 1), // ImageGather
            new(1, 1, 3, 1), // ImageDrefGather
            new(1, 1, 2, 1), // ImageRead
            new(0, 0, 3, 1), // ImageWrite
            new(1, 1, 1, 0), // Image
            new(1, 1, 1, 0), // ImageQueryFormat
            new(1, 1, 1, 0), // ImageQueryOrder
            new(1, 1, 2, 0), // ImageQuerySizeLod
            new(1, 1, 1, 0), // ImageQuerySize
            new(1, 1, 2, 0), // ImageQueryLod
            new(1, 1, 1, 0), // ImageQueryLevels
            new(1, 1, 1, 0), // ImageQuerySamples
            new(1, 1, 0, 0), // #108
            new(1, 1, 1, 0), // ConvertFToU
            new(1, 1, 1, 0), // ConvertFToS
            new(1, 1, 1, 0), // ConvertSToF
            new(1, 1, 1, 0), // ConvertUToF
            new(1, 1, 1, 0), // UConvert
            new(1, 1, 1, 0), // SConvert
            new(1, 1, 1, 0), // FConvert
            new(1, 1, 1, 0), // QuantizeToF16
            new(1, 1, 1, 0), // ConvertPtrToU
            new(1, 1, 1, 0), // SatConvertSToU
            new(1, 1, 1, 0), // SatConvertUToS
            new(1, 1, 1, 0), // ConvertUToPtr
            new(1, 1, 1, 0), // PtrCastToGeneric
            new(1, 1, 1, 0), // GenericCastToPtr
            new(1, 1, 1, 1), // GenericCastToPtrExplicit
            new(1, 1, 1, 0), // Bitcast
            new(1, 1, 0, 0), // #125
            new(1, 1, 1, 0), // SNegate
            new(1, 1, 1, 0), // FNegate
            new(1, 1, 2, 0), // IAdd
            new(1, 1, 2, 0), // FAdd
            new(1, 1, 2, 0), // ISub
            new(1, 1, 2, 0), // FSub
            new(1, 1, 2, 0), // IMul
            new(1, 1, 2, 0), // FMul
            new(1, 1, 2, 0), // UDiv
            new(1, 1, 2, 0), // SDiv
            new(1, 1, 2, 0), // FDiv
            new(1, 1, 2, 0), // UMod
            new(1, 1, 2, 0), // SRem
            new(1, 1, 2, 0), // SMod
            new(1, 1, 2, 0), // FRem
            new(1, 1, 2, 0), // FMod
            new(1, 1, 2, 0), // VectorTimesScalar
            new(1, 1, 2, 0), // MatrixTimesScalar
            new(1, 1, 2, 0), // VectorTimesMatrix
            new(1, 1, 2, 0), // MatrixTimesVector
            new(1, 1, 2, 0), // MatrixTimesMatrix
            new(1, 1, 2, 0), // OuterProduct
            new(1, 1, 2, 0), // Dot
            new(1, 1, 2, 0), // IAddCarry
            new(1, 1, 2, 0), // ISubBorrow
            new(1, 1, 2, 0), // UMulExtended
            new(1, 1, 2, 0), // SMulExtended
            new(1, 1, 0, 0), // #153
            new(1, 1, 1, 0), // Any
            new(1, 1, 1, 0), // All
            new(1, 1, 1, 0), // IsNan
            new(1, 1, 1, 0), // IsInf
            new(1, 1, 1, 0), // IsFinite
            new(1, 1, 1, 0), // IsNormal
            new(1, 1, 1, 0), // SignBitSet
            new(1, 1, 2, 0), // LessOrGreater
            new(1, 1, 2, 0), // Ordered
            new(1, 1, 2, 0), // Unordered
            new(1, 1, 2, 0), // LogicalEqual
            new(1, 1, 2, 0), // LogicalNotEqual
            new(1, 1, 2, 0), // LogicalOr
            new(1, 1, 2, 0), // LogicalAnd
            new(1, 1, 1, 0), // LogicalNot
            new(1, 1, 3, 0), // Select
            new(1, 1, 2, 0), // IEqual
            new(1, 1, 2, 0), // INotEqual
            new(1, 1, 2, 0), // UGreaterThan
            new(1, 1, 2, 0), // SGreaterThan
            new(1, 1, 2, 0), // UGreaterThanEqual
            new(1, 1, 2, 0), // SGreaterThanEqual
            new(1, 1, 2, 0), // ULessThan
            new(1, 1, 2, 0), // SLessThan
            new(1, 1, 2, 0), // ULessThanEqual
            new(1, 1, 2, 0), // SLessThanEqual
            new(1, 1, 2, 0), // FOrdEqual
            new(1, 1, 2, 0), // FUnordEqual
            new(1, 1, 2, 0), // FOrdNotEqual
            new(1, 1, 2, 0), // FUnordNotEqual
            new(1, 1, 2, 0), // FOrdLessThan
            new(1, 1, 2, 0), // FUnordLessThan
            new(1, 1, 2, 0), // FOrdGreaterThan
            new(1, 1, 2, 0), // FUnordGreaterThan
            new(1, 1, 2, 0), // FOrdLessThanEqual
            new(1, 1, 2, 0), // FUnordLessThanEqual
            new(1, 1, 2, 0), // FOrdGreaterThanEqual
            new(1, 1, 2, 0), // FUnordGreaterThanEqual
            new(1, 1, 0, 0), // #192
            new(1, 1, 0, 0), // #193
            new(1, 1, 2, 0), // ShiftRightLogical
            new(1, 1, 2, 0), // ShiftRightArithmetic
            new(1, 1, 2, 0), // ShiftLeftLogical
            new(1, 1, 2, 0), // BitwiseOr
            new(1, 1, 2, 0), // BitwiseXor
            new(1, 1, 2, 0), // BitwiseAnd
            new(1, 1, 1, 0), // Not
            new(1, 1, 4, 0), // BitFieldInsert
            new(1, 1, 3, 0), // BitFieldSExtract
            new(1, 1, 3, 0), // BitFieldUExtract
            new(1, 1, 1, 0), // BitReverse
            new(1, 1, 1, 0), // BitCount
            new(1, 1, 0, 0), // #206
            new(1, 1, 0, 0), // DPdx
            new(1, 1, 0, 0), // DPdy
            new(1, 1, 0, 0), // Fwidth
            new(1, 1, 0, 0), // DPdxFine
            new(1, 1, 0, 0), // DPdyFine
            new(1, 1, 0, 0), // FwidthFine
            new(1, 1, 0, 0), // DPdxCoarse
            new(1, 1, 0, 0), // DPdyCoarse
            new(1, 1, 0, 0), // FwidthCoarse
            new(1, 1, 0, 0), // #216
            new(1, 1, 0, 0), // #217
            new(0, 0, 0, 0), // EmitVertex
            new(0, 0, 0, 0), // EndPrimitive
            new(0, 0, 0, 0), // EmitStreamVertex
            new(0, 0, 0, 0), // EndStreamPrimitive
            new(1, 1, 0, 0), // #222
            new(1, 1, 0, 0), // #223
            new(0, 0, 3, 0), // ControlBarrier
            new(0, 0, 2, 0), // MemoryBarrier
            new(1, 1, 0, 0), // #226
            new(1, 1, 0, 0), // AtomicLoad
            new(0, 0, 0, 0), // AtomicStore
            new(1, 1, 0, 0), // AtomicExchange
            new(1, 1, 0, 0), // AtomicCompareExchange
            new(1, 1, 0, 0), // AtomicCompareExchangeWeak
            new(1, 1, 0, 0), // AtomicIIncrement
            new(1, 1, 0, 0), // AtomicIDecrement
            new(1, 1, 0, 0), // AtomicIAdd
            new(1, 1, 0, 0), // AtomicISub
            new(1, 1, 0, 0), // AtomicSMin
            new(1, 1, 0, 0), // AtomicUMin
            new(1, 1, 0, 0), // AtomicSMax
            new(1, 1, 0, 0), // AtomicUMax
            new(1, 1, 0, 0), // AtomicAnd
            new(1, 1, 0, 0), // AtomicOr
            new(1, 1, 0, 0), // AtomicXor
            new(1, 1, 0, 0), // #243
            new(1, 1, 0, 0), // #244
            new(1, 1, 0, 0), // Phi
            new(0, 0, 2, 1), // LoopMerge
            new(0, 0, 1, 1), // SelectionMerge
            new(1, 0, 0, 0), // Label
            new(0, 0, 1, 0), // Branch
            new(0, 0, 3, 1), // BranchConditional
            new(0, 0, 0, 0), // Switch
            new(0, 0, 0, 0), // Kill
            new(0, 0, 0, 0), // Return
            new(0, 0, 0, 0), // ReturnValue
            new(0, 0, 0, 0), // Unreachable
            new(0, 0, 0, 0), // LifetimeStart
            new(0, 0, 0, 0), // LifetimeStop
            new(1, 1, 0, 0), // #258
            new(1, 1, 0, 0), // GroupAsyncCopy
            new(0, 0, 0, 0), // GroupWaitEvents
            new(1, 1, 0, 0), // GroupAll
            new(1, 1, 0, 0), // GroupAny
            new(1, 1, 0, 0), // GroupBroadcast
            new(1, 1, 0, 0), // GroupIAdd
            new(1, 1, 0, 0), // GroupFAdd
            new(1, 1, 0, 0), // GroupFMin
            new(1, 1, 0, 0), // GroupUMin
            new(1, 1, 0, 0), // GroupSMin
            new(1, 1, 0, 0), // GroupFMax
            new(1, 1, 0, 0), // GroupUMax
            new(1, 1, 0, 0), // GroupSMax
            new(1, 1, 0, 0), // #272
            new(1, 1, 0, 0), // #273
            new(1, 1, 0, 0), // ReadPipe
            new(1, 1, 0, 0), // WritePipe
            new(1, 1, 0, 0), // ReservedReadPipe
            new(1, 1, 0, 0), // ReservedWritePipe
            new(1, 1, 0, 0), // ReserveReadPipePackets
            new(1, 1, 0, 0), // ReserveWritePipePackets
            new(0, 0, 0, 0), // CommitReadPipe
            new(0, 0, 0, 0), // CommitWritePipe
            new(1, 1, 0, 0), // IsValidReserveId
            new(1, 1, 0, 0), // GetNumPipePackets
            new(1, 1, 0, 0), // GetMaxPipePackets
            new(1, 1, 0, 0), // GroupReserveReadPipePackets
            new(1, 1, 0, 0), // GroupReserveWritePipePackets
            new(0, 0, 0, 0), // GroupCommitReadPipe
            new(0, 0, 0, 0), // GroupCommitWritePipe
            new(1, 1, 0, 0), // #289
            new(1, 1, 0, 0), // #290
            new(1, 1, 0, 0), // EnqueueMarker
            new(1, 1, 0, 0), // EnqueueKernel
            new(1, 1, 0, 0), // GetKernelNDrangeSubGroupCount
            new(1, 1, 0, 0), // GetKernelNDrangeMaxSubGroupSize
            new(1, 1, 0, 0), // GetKernelWorkGroupSize
            new(1, 1, 0, 0), // GetKernelPreferredWorkGroupSizeMultiple
            new(0, 0, 0, 0), // RetainEvent
            new(0, 0, 0, 0), // ReleaseEvent
            new(1, 1, 0, 0), // CreateUserEvent
            new(1, 1, 0, 0), // IsValidEvent
            new(0, 0, 0, 0), // SetUserEventStatus
            new(0, 0, 0, 0), // CaptureEventProfilingInfo
            new(1, 1, 0, 0), // GetDefaultQueue
            new(1, 1, 0, 0), // BuildNDRange
            new(1, 1, 2, 1), // ImageSparseSampleImplicitLod
            new(1, 1, 2, 1), // ImageSparseSampleExplicitLod
            new(1, 1, 3, 1), // ImageSparseSampleDrefImplicitLod
            new(1, 1, 3, 1), // ImageSparseSampleDrefExplicitLod
            new(1, 1, 2, 1), // ImageSparseSampleProjImplicitLod
            new(1, 1, 2, 1), // ImageSparseSampleProjExplicitLod
            new(1, 1, 3, 1), // ImageSparseSampleProjDrefImplicitLod
            new(1, 1, 3, 1), // ImageSparseSampleProjDrefExplicitLod
            new(1, 1, 2, 1), // ImageSparseFetch
            new(1, 1, 3, 1), // ImageSparseGather
            new(1, 1, 3, 1), // ImageSparseDrefGather
            new(1, 1, 1, 0), // ImageSparseTexelsResident
            new(0, 0, 0, 0), // NoLine
            new(1, 1, 0, 0), // AtomicFlagTestAndSet
            new(0, 0, 0, 0), // AtomicFlagClear
            new(1, 1, 0, 0), // ImageSparseRead
            new(1, 1, 0, 0), // SizeOf
            new(1, 1, 0, 0), // TypePipeStorage
            new(1, 1, 0, 0), // ConstantPipeStorage
            new(1, 1, 0, 0), // CreatePipeFromPipeStorage
            new(1, 1, 0, 0), // GetKernelLocalSizeForSubgroupCount
            new(1, 1, 0, 0), // GetKernelMaxNumSubgroups
            new(1, 1, 0, 0), // TypeNamedBarrier
            new(1, 1, 0, 1), // NamedBarrierInitialize
            new(0, 0, 2, 1), // MemoryNamedBarrier
            new(1, 1, 0, 0), // ModuleProcessed
            new(0, 0, 0, 1), // ExecutionModeId
            new(0, 0, 0, 1), // DecorateId
            new(1, 1, 1, 1), // GroupNonUniformElect
            new(1, 1, 1, 1), // GroupNonUniformAll
            new(1, 1, 1, 1), // GroupNonUniformAny
            new(1, 1, 1, 1), // GroupNonUniformAllEqual
            new(1, 1, 1, 1), // GroupNonUniformBroadcast
            new(1, 1, 1, 1), // GroupNonUniformBroadcastFirst
            new(1, 1, 1, 1), // GroupNonUniformBallot
            new(1, 1, 1, 1), // GroupNonUniformInverseBallot
            new(1, 1, 1, 1), // GroupNonUniformBallotBitExtract
            new(1, 1, 1, 1), // GroupNonUniformBallotBitCount
            new(1, 1, 1, 1), // GroupNonUniformBallotFindLSB
            new(1, 1, 1, 1), // GroupNonUniformBallotFindMSB
            new(1, 1, 1, 1), // GroupNonUniformShuffle
            new(1, 1, 1, 1), // GroupNonUniformShuffleXor
            new(1, 1, 1, 1), // GroupNonUniformShuffleUp
            new(1, 1, 1, 1), // GroupNonUniformShuffleDown
            new(1, 1, 1, 1), // GroupNonUniformIAdd
            new(1, 1, 1, 1), // GroupNonUniformFAdd
            new(1, 1, 1, 1), // GroupNonUniformIMul
            new(1, 1, 1, 1), // GroupNonUniformFMul
            new(1, 1, 1, 1), // GroupNonUniformSMin
            new(1, 1, 1, 1), // GroupNonUniformUMin
            new(1, 1, 1, 1), // GroupNonUniformFMin
            new(1, 1, 1, 1), // GroupNonUniformSMax
            new(1, 1, 1, 1), // GroupNonUniformUMax
            new(1, 1, 1, 1), // GroupNonUniformFMax
            new(1, 1, 1, 1), // GroupNonUniformBitwiseAnd
            new(1, 1, 1, 1), // GroupNonUniformBitwiseOr
            new(1, 1, 1, 1), // GroupNonUniformBitwiseXor
            new(1, 1, 1, 1), // GroupNonUniformLogicalAnd
            new(1, 1, 1, 1), // GroupNonUniformLogicalOr
            new(1, 1, 1, 1), // GroupNonUniformLogicalXor
            new(1, 1, 1, 1), // GroupNonUniformQuadBroadcast
            new(1, 1, 1, 1), // GroupNonUniformQuadSwap
        };

        /// <summary>Scans <paramref name="compiledData"/> (see class summary for why it may contain more than one
        /// SMOL-V module) for the vertex and fragment modules, decodes both back into raw SPIR-V, and returns them.
        /// Fails unless both stages are found.</summary>
        public static bool TryDecodeStages(byte[] compiledData, out byte[] vertexSpirv, out byte[] fragmentSpirv, out string error)
        {
            vertexSpirv = null;
            fragmentSpirv = null;
            error = null;

            var offset = FindFirstModuleOffset(compiledData);
            if (offset < 0)
            {
                error = "Could not find a SMOL-V module header in the compiled Vulkan shader data.";
                return false;
            }

            while (offset >= 0 && offset + 24 <= compiledData.Length && (vertexSpirv == null || fragmentSpirv == null))
            {
                if (!TryDecodeModule(compiledData, offset, out var moduleSpirv, out var consumed, out _))
                {
                    offset = FindFirstModuleOffset(compiledData, offset + 1);
                    continue;
                }

                if (TryGetEntryPointExecutionModel(moduleSpirv, out var executionModel))
                {
                    if (executionModel == ExecutionModelVertex && vertexSpirv == null) vertexSpirv = moduleSpirv;
                    else if (executionModel == ExecutionModelFragment && fragmentSpirv == null) fragmentSpirv = moduleSpirv;
                }

                var nextOffset = offset + consumed;
                if (nextOffset + 4 > compiledData.Length || ReadUInt32(compiledData, nextOffset) != SmolHeaderMagic)
                {
                    // Modules are expected to be back-to-back with no padding - fall back to scanning if that ever
                    // isn't true, rather than silently reading garbage as the next module's header.
                    nextOffset = FindFirstModuleOffset(compiledData, nextOffset);
                }

                offset = nextOffset;
            }

            if (vertexSpirv == null || fragmentSpirv == null)
            {
                var missing = vertexSpirv == null && fragmentSpirv == null ? "vertex or fragment"
                    : vertexSpirv == null ? "vertex" : "fragment";
                error = $"Could not find a {missing} entry point among the compiled Vulkan shader data's SMOL-V module(s).";
                vertexSpirv = null;
                fragmentSpirv = null;
                return false;
            }

            return true;
        }

        private static int FindFirstModuleOffset(byte[] data, int searchStart = 0)
        {
            for (var i = searchStart; i + 24 <= data.Length; i++)
            {
                if (ReadUInt32(data, i) == SmolHeaderMagic) return i;
            }

            return -1;
        }

        private static bool TryGetEntryPointExecutionModel(byte[] spirv, out uint executionModel)
        {
            executionModel = 0;
            var wordCount = spirv.Length / 4;
            var i = 5; // skip the 5-word SPIR-V header
            while (i < wordCount)
            {
                var instrLen = ReadUInt32(spirv, i * 4) >> 16;
                var op = ReadUInt32(spirv, i * 4) & 0xFFFF;
                if (instrLen == 0 || i + instrLen > wordCount) return false;

                if (op == OpEntryPoint && instrLen >= 2)
                {
                    executionModel = ReadUInt32(spirv, (i + 1) * 4);
                    return true;
                }

                i += (int)instrLen;
            }

            return false;
        }

        /// <summary>Decodes one SMOL-V module starting at byte <paramref name="offset"/> of <paramref name="data"/>
        /// back into raw SPIR-V, faithfully porting <c>smolv::Decode</c> from smolv.cpp. Returns the number of input
        /// bytes the module occupied via <paramref name="consumedBytes"/>, so the caller can find the next
        /// concatenated module (if any).</summary>
        private static bool TryDecodeModule(byte[] data, int offset, out byte[] spirv, out int consumedBytes, out string error)
        {
            spirv = null;
            consumedBytes = 0;
            error = null;

            if (!TryCheckSmolHeader(data, offset, data.Length - offset))
            {
                error = "Invalid or unsupported SMOL-V header.";
                return false;
            }

            var decodedSizeRaw = ReadUInt32(data, offset + 20);
            var maxBytes = Math.Min(int.MaxValue, (long)(data.Length - offset) * 64);
            if (decodedSizeRaw < 20 || decodedSizeRaw % 4 != 0 || decodedSizeRaw > maxBytes)
            {
                error = "Invalid SMOL-V decoded size.";
                return false;
            }

            var decodedSize = (int)decodedSizeRaw;
            var outBytes = new byte[decodedSize];
            var outPos = 0;
            var pos = offset;

            unchecked
            {
                WriteUInt32(outBytes, ref outPos, SpirvHeaderMagic);
                pos += 4;
                var versionWord = ReadUInt32(data, pos);
                var smolVersion = (int)(versionWord >> 24);
                WriteUInt32(outBytes, ref outPos, versionWord & 0x00FFFFFF); // SPIR-V version
                pos += 4;
                WriteUInt32(outBytes, ref outPos, ReadUInt32(data, pos));
                pos += 4; // generator
                WriteUInt32(outBytes, ref outPos, ReadUInt32(data, pos));
                pos += 4; // bound
                WriteUInt32(outBytes, ref outPos, ReadUInt32(data, pos));
                pos += 4; // schema
                pos += 4; // decoded size field, already read above

                var knownOpsCount = smolVersion == 0 ? OpModuleProcessed + 1 : OpGroupNonUniformQuadSwap + 1;
                var dataEnd = data.Length;
                uint prevResult = 0;
                uint prevDecorate = 0;

                while (outPos < decodedSize)
                {
                    if (!TryReadLengthOp(data, ref pos, dataEnd, out var instrLen, out var op))
                    {
                        error = "Malformed SMOL-V instruction stream (length/op).";
                        return false;
                    }

                    var wasSwizzle = op == OpVectorShuffleCompact;
                    if (wasSwizzle) op = OpVectorShuffle;
                    WriteUInt32(outBytes, ref outPos, (uint)((instrLen << 16) | (uint)op));

                    var ioffs = 1u;

                    if (OpHasType(op, knownOpsCount))
                    {
                        if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                        {
                            error = "Truncated SMOL-V stream (type).";
                            return false;
                        }

                        WriteUInt32(outBytes, ref outPos, val);
                        ioffs++;
                    }

                    if (OpHasResult(op, knownOpsCount))
                    {
                        if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                        {
                            error = "Truncated SMOL-V stream (result).";
                            return false;
                        }

                        prevResult += ZigDecode(val);
                        WriteUInt32(outBytes, ref outPos, prevResult);
                        ioffs++;
                    }

                    if (op == OpDecorate || op == OpMemberDecorate)
                    {
                        if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                        {
                            error = "Truncated SMOL-V stream (decorate).";
                            return false;
                        }

                        prevDecorate += ZigDecode(val);
                        WriteUInt32(outBytes, ref outPos, prevDecorate);
                        ioffs++;
                    }

                    if (op == OpMemberDecorate)
                    {
                        if (pos >= dataEnd)
                        {
                            error = "Truncated SMOL-V stream (member decorate count).";
                            return false;
                        }

                        int count = data[pos++];
                        uint prevIndex = 0;
                        uint prevOffset = 0;
                        for (var m = 0; m < count; m++)
                        {
                            if (!TryReadVarint(data, ref pos, dataEnd, out var memberIndex))
                            {
                                error = "Truncated SMOL-V stream (member index).";
                                return false;
                            }

                            prevIndex += memberIndex;
                            memberIndex = prevIndex;

                            if (!TryReadVarint(data, ref pos, dataEnd, out var memberDec))
                            {
                                error = "Truncated SMOL-V stream (member decoration).";
                                return false;
                            }

                            var knownExtraOps = DecorationExtraOps(memberDec);
                            uint memberLen;
                            if (knownExtraOps == -1)
                            {
                                if (!TryReadVarint(data, ref pos, dataEnd, out memberLen))
                                {
                                    error = "Truncated SMOL-V stream (member length).";
                                    return false;
                                }

                                memberLen += 4;
                            }
                            else
                            {
                                memberLen = (uint)(4 + knownExtraOps);
                            }

                            if (m != 0)
                            {
                                WriteUInt32(outBytes, ref outPos, (memberLen << 16) | (uint)op);
                                WriteUInt32(outBytes, ref outPos, prevDecorate);
                            }

                            WriteUInt32(outBytes, ref outPos, memberIndex);
                            WriteUInt32(outBytes, ref outPos, memberDec);

                            if (memberDec == 35) // Offset
                            {
                                if (memberLen != 5)
                                {
                                    error = "Malformed SMOL-V member Offset decoration.";
                                    return false;
                                }

                                if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                                {
                                    error = "Truncated SMOL-V stream (member offset).";
                                    return false;
                                }

                                prevOffset += val;
                                WriteUInt32(outBytes, ref outPos, prevOffset);
                            }
                            else
                            {
                                for (var i = 4u; i < memberLen; i++)
                                {
                                    if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                                    {
                                        error = "Truncated SMOL-V stream (member extra word).";
                                        return false;
                                    }

                                    WriteUInt32(outBytes, ref outPos, val);
                                }
                            }
                        }

                        continue;
                    }

                    var relativeCount = OpDeltaFromResult(op, knownOpsCount);
                    for (var i = 0; i < relativeCount && ioffs < instrLen; i++, ioffs++)
                    {
                        if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                        {
                            error = "Truncated SMOL-V stream (relative id).";
                            return false;
                        }

                        WriteUInt32(outBytes, ref outPos, prevResult - ZigDecode(val));
                    }

                    if (wasSwizzle && instrLen <= 9)
                    {
                        if (pos >= dataEnd)
                        {
                            error = "Truncated SMOL-V stream (swizzle).";
                            return false;
                        }

                        var swizzle = data[pos++];
                        if (instrLen > 5) WriteUInt32(outBytes, ref outPos, (uint)(swizzle >> 6) & 3);
                        if (instrLen > 6) WriteUInt32(outBytes, ref outPos, (uint)(swizzle >> 4) & 3);
                        if (instrLen > 7) WriteUInt32(outBytes, ref outPos, (uint)(swizzle >> 2) & 3);
                        if (instrLen > 8) WriteUInt32(outBytes, ref outPos, (uint)swizzle & 3);
                    }
                    else if (OpVarRest(op, knownOpsCount))
                    {
                        for (; ioffs < instrLen; ioffs++)
                        {
                            if (!TryReadVarint(data, ref pos, dataEnd, out var val))
                            {
                                error = "Truncated SMOL-V stream (var rest).";
                                return false;
                            }

                            WriteUInt32(outBytes, ref outPos, val);
                        }
                    }
                    else
                    {
                        for (; ioffs < instrLen; ioffs++)
                        {
                            if (pos + 4 > dataEnd)
                            {
                                error = "Truncated SMOL-V stream (raw word).";
                                return false;
                            }

                            WriteUInt32(outBytes, ref outPos, ReadUInt32(data, pos));
                            pos += 4;
                        }
                    }
                }
            }

            if (outPos != decodedSize)
            {
                error = "SMOL-V decode did not produce the expected output size.";
                return false;
            }

            spirv = outBytes;
            consumedBytes = pos - offset;
            return true;
        }

        private static bool TryCheckSmolHeader(byte[] data, int offset, int byteCount)
        {
            if (byteCount < 24) return false;
            if (ReadUInt32(data, offset) != SmolHeaderMagic) return false;
            var headerVersion = ReadUInt32(data, offset + 4) & 0x00FFFFFF;
            if (headerVersion < 0x00010000 || headerVersion > 0x00010600) return false;
            var smolVersion = ReadUInt32(data, offset + 4) >> 24;
            return smolVersion <= 1;
        }

        private static bool OpHasResult(int op, int opsCount)
        {
            return op >= 0 && op < opsCount && OpTable[op].HasResult != 0;
        }

        private static bool OpHasType(int op, int opsCount)
        {
            return op >= 0 && op < opsCount && OpTable[op].HasType != 0;
        }

        private static int OpDeltaFromResult(int op, int opsCount)
        {
            return op >= 0 && op < opsCount ? OpTable[op].DeltaFromResult : 0;
        }

        private static bool OpVarRest(int op, int opsCount)
        {
            return op >= 0 && op < opsCount && OpTable[op].VarRest != 0;
        }

        private static int DecorationExtraOps(uint dec)
        {
            if (dec == 0 || (dec >= 2 && dec <= 5)) return 0; // RelaxedPrecision, Block..ColMajor
            if (dec >= 29 && dec <= 37) return 1; // Stream..XfbStride
            return -1;
        }

        private static int RemapOp(int op)
        {
            switch (op)
            {
                case OpDecorate: return OpNop;
                case OpNop: return OpDecorate;
                case OpLoad: return OpUndef;
                case OpUndef: return OpLoad;
                case OpStore: return OpSourceContinued;
                case OpSourceContinued: return OpStore;
                case OpAccessChain: return OpSource;
                case OpSource: return OpAccessChain;
                case OpVectorShuffle: return OpSourceExtension;
                case OpSourceExtension: return OpVectorShuffle;
                case OpMemberDecorate: return OpString;
                case OpString: return OpMemberDecorate;
                case OpLabel: return OpLine;
                case OpLine: return OpLabel;
                case OpVariable: return OpUnused9;
                case OpUnused9: return OpVariable;
                case OpFMul: return OpExtension;
                case OpExtension: return OpFMul;
                case OpFAdd: return OpExtInstImport;
                case OpExtInstImport: return OpFAdd;
                case OpTypePointer: return OpMemoryModel;
                case OpMemoryModel: return OpTypePointer;
                case OpFNegate: return OpEntryPoint;
                case OpEntryPoint: return OpFNegate;
                default: return op;
            }
        }

        private static uint DecodeLen(int op, uint len)
        {
            len++;
            if (op == OpVectorShuffle) len += 4;
            if (op == OpVectorShuffleCompact) len += 4;
            if (op == OpDecorate) len += 2;
            if (op == OpLoad) len += 3;
            if (op == OpAccessChain) len += 3;
            return len;
        }

        private static bool TryReadLengthOp(byte[] data, ref int pos, int dataEnd, out uint outLen, out int outOp)
        {
            outLen = 0;
            outOp = 0;
            if (!TryReadVarint(data, ref pos, dataEnd, out var val)) return false;

            outLen = ((val >> 20) << 4) | ((val >> 4) & 0xF);
            outOp = (int)(((val >> 4) & 0xFFF0) | (val & 0xF));
            outOp = RemapOp(outOp);
            outLen = DecodeLen(outOp, outLen);
            return true;
        }

        private static bool TryReadVarint(byte[] data, ref int pos, int dataEnd, out uint outVal)
        {
            uint v = 0;
            var shift = 0;
            outVal = 0;
            while (true)
            {
                if (pos >= dataEnd) return false;
                var b = data[pos];
                v |= (uint)(b & 127) << shift;
                shift += 7;
                pos++;
                if ((b & 128) == 0) break;
            }

            outVal = v;
            return true;
        }

        private static uint ZigDecode(uint u)
        {
            return (u & 1) != 0 ? ~(u >> 1) : u >> 1;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static void WriteUInt32(byte[] buffer, ref int pos, uint value)
        {
            if (pos + 4 <= buffer.Length)
            {
                buffer[pos] = (byte)value;
                buffer[pos + 1] = (byte)(value >> 8);
                buffer[pos + 2] = (byte)(value >> 16);
                buffer[pos + 3] = (byte)(value >> 24);
            }

            pos += 4;
        }
    }
}