// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;

// IEEE CRC-32 (not CRC32C). Input/output state is unfinalized so callers can
// update a stream in chunks, starting at 0xFFFFFFFF and XORing once at the end.
static class FastCrc32 {
    static readonly uint[][] Tables=CreateTables();

    static uint[][] CreateTables(){
        var tables=new uint[8][];
        for(int slice=0;slice<tables.Length;slice++)tables[slice]=new uint[256];
        for(int index=0;index<256;index++){
            uint value=(uint)index;
            for(int bit=0;bit<8;bit++)value=(value&1)!=0?(value>>1)^0xEDB88320U:value>>1;
            tables[0][index]=value;
        }
        for(int slice=1;slice<tables.Length;slice++)for(int index=0;index<256;index++){
            uint previous=tables[slice-1][index];
            tables[slice][index]=tables[0][previous&255]^(previous>>8);
        }
        return tables;
    }

    internal static uint Update(uint runningCrc,byte[] buffer,int offset,int count){
        if(buffer==null)throw new ArgumentNullException("buffer");
        if(offset<0||offset>buffer.Length)throw new ArgumentOutOfRangeException("offset");
        if(count<0||count>buffer.Length-offset)throw new ArgumentOutOfRangeException("count");
        int end=offset+count;
        var t0=Tables[0];var t1=Tables[1];var t2=Tables[2];var t3=Tables[3];
        var t4=Tables[4];var t5=Tables[5];var t6=Tables[6];var t7=Tables[7];
        while(end-offset>=8){
            uint first=(uint)buffer[offset]|((uint)buffer[offset+1]<<8)|((uint)buffer[offset+2]<<16)|((uint)buffer[offset+3]<<24);
            uint second=(uint)buffer[offset+4]|((uint)buffer[offset+5]<<8)|((uint)buffer[offset+6]<<16)|((uint)buffer[offset+7]<<24);
            first^=runningCrc;
            runningCrc=t7[first&255]^t6[(first>>8)&255]^t5[(first>>16)&255]^t4[first>>24]
                ^t3[second&255]^t2[(second>>8)&255]^t1[(second>>16)&255]^t0[second>>24];
            offset+=8;
        }
        while(offset<end)runningCrc=t0[(runningCrc^buffer[offset++])&255]^(runningCrc>>8);
        return runningCrc;
    }
}
