﻿(*** hide ***)
module Tutorial.Fs.examples.genericScan.Sum

open Microsoft.FSharp.Quotations
open Alea.CUDA
open Alea.CUDA.Utilities
open Plan

(**
Sum multi-scan function for all warps in the block. 

Specialized version for sum without expression splicing and slightly more efficient implementation based on inclusive multiscan.
*)
let inline multiScan numWarps logNumWarps =
    let warpStride = WARP_SIZE + WARP_SIZE / 2 + 1
    <@ fun tid (x:'T) (totalRef:'T ref) ->
        let warp = tid / WARP_SIZE
        let lane = tid &&& (WARP_SIZE - 1)
    
        // Allocate shared memory.
        let shared = __shared__.Array<'T>(numWarps * warpStride) |> __array_to_ptr |> __ptr_volatile
        let totalsShared = __shared__.Array<'T>(2 * numWarps) |> __array_to_ptr |> __ptr_volatile 

        let warpShared = (shared + warp * warpStride).Volatile()
        let s = (warpShared + lane + WARP_SIZE / 2).Volatile()
        warpShared.[lane] <- 0G
        s.[0] <- x

        // Run inclusive scan on each warp's data.
        let mutable scan = x |> __unbox
        for i = 0 to LOG_WARP_SIZE - 1 do
            let offset = 1 <<< i
            scan <- scan + s.[-offset]   
            if i < LOG_WARP_SIZE - 1 then s.[0] <- scan
        
        if lane = WARP_SIZE - 1 then
            totalsShared.[numWarps + warp] <- scan  

        // Synchronize to make all the totals available to the reduction code.
        __syncthreads()

        if tid < numWarps then
            // Grab the block total for the tid'th block. This is the last element
            // in the block's scanned sequence. This operation avoids bank conflicts.
            let total = totalsShared.[numWarps + tid]
            totalsShared.[tid] <- 0G
            let s = (totalsShared + numWarps + tid).Volatile()  

            let mutable totalsScan = total |> __unbox
            for i = 0 to logNumWarps - 1 do
                let offset = 1 <<< i
                totalsScan <- totalsScan + s.[-offset]
                s.[0] <- totalsScan

            // Store totalsScan shifted by one to the right for an exclusive scan.
            //if 0 < tid && tid < numWarps - 1 then totalsShared.[tid + 1] <- totalsScan 
            //if tid = 0 then totalsShared.[tid] <- init()
            totalsShared.[tid + 1] <- totalsScan

        // Synchronize to make the block scan available to all warps.
        __syncthreads()

        // The total is the last element.
        totalRef := totalsShared.[2 * numWarps - 1]

        // Add the block scan to the inclusive scan for the block.
        scan + totalsShared.[warp] @>

(**
Multi-scan function for all warps in the block.
*)
let inline multiScanExcl numWarps logNumWarps =
    let warpStride = WARP_SIZE + WARP_SIZE / 2 + 1

    <@ fun tid (x:'T) (totalRef : 'T ref) ->
        let warp = tid / WARP_SIZE
        let lane = tid &&& (WARP_SIZE - 1)
    
        // Allocate shared memory.
        let shared = __shared__.Array<'T>(numWarps * warpStride) |> __array_to_ptr |> __ptr_volatile
        let totalsShared = __shared__.Array<'T>(2 * numWarps) |> __array_to_ptr |> __ptr_volatile 
        let exclScan = __shared__.Array<'T>(numWarps * WARP_SIZE + 1) |> __array_to_ptr |> __ptr_volatile

        let warpShared = (shared + warp * warpStride).Volatile()
        let s = (warpShared + lane + WARP_SIZE / 2).Volatile()
        warpShared.[lane] <- 0G
        s.[0] <- x

        // Run inclusive scan on each warp's data.
        let mutable scan = x |> __unbox
        for i = 0 to LOG_WARP_SIZE - 1 do
            let offset = 1 <<< i
            scan <- scan + s.[-offset]   
            if i < LOG_WARP_SIZE - 1 then s.[0] <- scan
        
        if lane = WARP_SIZE - 1 then
            totalsShared.[numWarps + warp] <- scan  

        // Synchronize to make all the totals available to the reduction code.
        __syncthreads()

        if tid < numWarps then
            // Grab the block total for the tid'th block. This is the last element
            // in the block's scanned sequence. This operation avoids bank conflicts.
            let total = totalsShared.[numWarps + tid]
            totalsShared.[tid] <- 0G
            let s = (totalsShared + numWarps + tid).Volatile()  

            let mutable totalsScan = total |> __unbox
            for i = 0 to logNumWarps - 1 do
                let offset = 1 <<< i
                totalsScan <- totalsScan + s.[-offset]
                s.[0] <- totalsScan

            // Store totalsScan shifted by one to the right for an exclusive scan.
            //if 0 < tid && tid < numWarps - 1 then totalsShared.[tid + 1] <- totalsScan 
            //if tid = 0 then totalsShared.[tid] <- init()
            totalsShared.[tid + 1] <- totalsScan

        // Synchronize to make the block scan available to all warps.
        __syncthreads()

        // The total is the last element.
        totalRef := totalsShared.[2 * numWarps - 1]

        // Add the block scan to the inclusive scan for the block.
        if tid = 0 then exclScan.[tid] <- 0G
        exclScan.[tid + 1] <- totalsShared.[warp] + scan

        // Synchronize to make the exclusive scan available to all threads.
        __syncthreads()

        exclScan.[tid] @>

(**
Exclusive scan of range totals.
*)
let inline scanReduceKernel (plan:Plan)  =
    let numThreads = plan.NumThreadsReduction
    let numWarps = plan.NumWarpsReduction
    let logNumWarps = log2 numWarps
    let multiScan = multiScan numWarps logNumWarps

    <@ fun numRanges (dRangeTotals:deviceptr<'T>) ->
        let tid = threadIdx.x
        let x = if tid < numRanges then dRangeTotals.[tid] else 0G
        let total:ref<'T> = ref 0G
        let sum = (%multiScan) tid x total
        // Shift the value from the inclusive scan for the exclusive scan.
        if tid < numRanges then dRangeTotals.[tid + 1] <- sum
        // Have the first thread in the block set the scan total.
        if tid = 0 then dRangeTotals.[0] <- 0G @>

(**
Sum-specialized generic scan downsweep kernel.
*)
let inline scanDownSweepKernel (plan:Plan) =
    let numWarps = plan.NumWarps
    let numValues = plan.NumValues
    let valuesPerThread = plan.ValuesPerThread
    let valuesPerWarp = plan.ValuesPerWarp 
    let logNumWarps = log2 numWarps
    let size = numWarps * valuesPerThread * (WARP_SIZE + 1)
    let multiScanExcl = multiScanExcl numWarps logNumWarps

    <@ fun (dValuesIn:deviceptr<'T>) (dValuesOut:deviceptr<'T>) (dRangeTotals:deviceptr<'T>) (dRanges:deviceptr<int>) (inclusive:int) ->
        let block = blockIdx.x
        let tid = threadIdx.x
        let warp = tid / WARP_SIZE
        let lane = (WARP_SIZE - 1) &&& tid
        let index = warp * valuesPerWarp + lane

        let mutable blockScan = dRangeTotals.[block]
        let mutable rangeX = dRanges.[block]
        let rangeY = dRanges.[block + 1]
            
        let shared = __shared__.Array<'T>(size) |> __array_to_ptr |> __ptr_volatile

        // Use a stride of 33 slots per warp per value to allow conflict-free transposes from strided to thread order.
        let warpShared = shared + warp * valuesPerThread * (WARP_SIZE + 1)
        let threadShared = warpShared + lane

        // Transpose values into thread order.
        let mutable offset = valuesPerThread * lane
        offset <- offset + offset / WARP_SIZE

        while rangeX < rangeY do

            for i = 0 to valuesPerThread - 1 do
                let source = rangeX + index + i * WARP_SIZE
                let x = if source < rangeY then dValuesIn.[source] else 0G
                threadShared.[i * (WARP_SIZE + 1)] <- x

            // Transpose into thread order by reading from transposeValues.
            // Compute the exclusive or inclusive scan of the thread values and their sum.
            let threadScan = __local__.Array<'T>(valuesPerThread)
            let mutable scan = 0G |> __unbox

            for i = 0 to valuesPerThread - 1 do 
                let x = warpShared.[offset + i]
                threadScan.[i] <- scan
                if (inclusive <> 0) then threadScan.[i] <- threadScan.[i] + x
                scan <- scan + x               
 
            // Exclusive multi-scan for each thread's scan offset within the block. 
            let localTotal:ref<'T> = ref 0G
            let localScan = (%multiScanExcl) tid scan localTotal
            let scanOffset = blockScan + localScan  
                
            // Apply the scan offset to each exclusive scan and put the values back into the shared memory they came out of.
            for i = 0 to valuesPerThread - 1 do
                let x = threadScan.[i] + scanOffset
                warpShared.[offset + i] <- x
                
            // Store the scan back to global memory.
            for i = 0 to valuesPerThread - 1 do
                let x = threadShared.[i * (WARP_SIZE + 1)]
                let target = rangeX + index + i * WARP_SIZE
                if target < rangeY then dValuesOut.[target] <- x

            // Grab the last element of totals_shared, which was set in Multiscan.
            // This is the total for all the values encountered in this pass.
            blockScan <- blockScan + !localTotal

            rangeX <- rangeX + numValues @>

