using System;
using System.Text;
namespace MusicRpc.Utils;
public static class StringUtils
{
    private static int FindLastCompleteCharIndex(byte[] buffer, int byteCount)
    {
        for (var position = byteCount - 1; position >= 0; position--)
        {
            if (buffer[position] < 0x80)
                return position;
            if ((buffer[position] & 0xC0) != 0x80) continue;
            var count = 0;
            while (position >= 0 && (buffer[position] & 0xC0) == 0x80)
            {
                position--;
                count++;
            }
            if (position < 0) continue;
            var lead = buffer[position];
            int required;
            switch (lead >> 4)
            {
                case 0b1100: required = 1; break; 
                case 0b1110: required = 2; break; 
                case 0b1111: required = 3; break; 
                default: continue; 
            }
            if (count == required)
                return position;
        }
        return -1;
    }
    public static string GetTruncatedStringByMaxByteLength(string str, int maxLength)
    {
        var strBuffer = Encoding.UTF8.GetBytes(str);
        if (strBuffer.Length < maxLength) return str;
        var truncatedLength = maxLength - 3;
        var truncatedBuffer = new byte[truncatedLength];
        Buffer.BlockCopy(strBuffer, 0, truncatedBuffer, 0, truncatedLength);
        var lastCompleteCharIndex = FindLastCompleteCharIndex(truncatedBuffer, truncatedLength);
        if (lastCompleteCharIndex == -1) return "Error";
        var truncatedString = new byte[lastCompleteCharIndex];
        Buffer.BlockCopy(truncatedBuffer, 0, truncatedString, 0, lastCompleteCharIndex);
        var ellipsisByteString = "..."u8.ToArray();
        var finalBuffer = new byte[truncatedString.Length + ellipsisByteString.Length];
        Buffer.BlockCopy(truncatedString, 0, finalBuffer, 0, truncatedString.Length);
        Buffer.BlockCopy(ellipsisByteString, 0, finalBuffer, truncatedString.Length, ellipsisByteString.Length);
        return Encoding.UTF8.GetString(finalBuffer);
    }
}