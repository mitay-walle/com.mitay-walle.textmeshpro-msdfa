#if TMP_MSDFA_UGUI_PATCHED
using System;
using System.Collections.Generic;

namespace TMPro
{
    internal sealed class TMP_MSDFAFont
    {
        private const int MaxCompositeDepth = 16;
        private const float EdgeColoringAngleThreshold = 3f;
        private const int EdgeLengthPrecision = 4;
        private const byte OnCurve = 0x01;
        private const byte XShortVector = 0x02;
        private const byte YShortVector = 0x04;
        private const byte Repeat = 0x08;
        private const byte XIsSameOrPositive = 0x10;
        private const byte YIsSameOrPositive = 0x20;
        private const int Black = 0;
        private const int Red = 1;
        private const int Green = 2;
        private const int Blue = 4;
        private const int Yellow = Red | Green;
        private const int Magenta = Red | Blue;
        private const int Cyan = Green | Blue;
        private const int White = Red | Green | Blue;
        private const int EdgeTypeLinear = 1;
        private const int EdgeTypeQuadratic = 2;

        private readonly byte[] m_Data;
        private readonly Dictionary<uint, GlyphShape> m_GlyphCache = new Dictionary<uint, GlyphShape>();
        private readonly uint[] m_Locations;
        private readonly uint m_GlyphTableOffset;
        private readonly ushort m_NumGlyphs;

        private readonly bool m_NormalizeContourWinding;

        private TMP_MSDFAFont(byte[] data, uint[] locations, uint glyphTableOffset, ushort unitsPerEm, ushort numGlyphs, bool normalizeContourWinding)
        {
            m_Data = data;
            m_Locations = locations;
            m_GlyphTableOffset = glyphTableOffset;
            UnitsPerEm = unitsPerEm;
            m_NumGlyphs = numGlyphs;
            m_NormalizeContourWinding = normalizeContourWinding;
        }

        internal ushort UnitsPerEm { get; }

        internal static bool TryCreate(byte[] data, bool normalizeContourWinding, out TMP_MSDFAFont font)
        {
            font = null;
            if (data == null || data.Length < 12)
                return false;

            if (TryReadTables(data, out Dictionary<string, TableRecord> tables) == false)
                return false;

            if (tables.TryGetValue("head", out TableRecord head) == false
                || tables.TryGetValue("maxp", out TableRecord maxp) == false
                || tables.TryGetValue("loca", out TableRecord loca) == false
                || tables.TryGetValue("glyf", out TableRecord glyf) == false)
                return false;

            if (head.Offset + 54 > data.Length || maxp.Offset + 6 > data.Length)
                return false;

            ushort unitsPerEm = ReadUInt16(data, head.Offset + 18);
            short indexToLocFormat = ReadInt16(data, head.Offset + 50);
            ushort numGlyphs = ReadUInt16(data, maxp.Offset + 4);
            uint[] locations = ReadLocations(data, loca, numGlyphs, indexToLocFormat);
            if (locations == null)
                return false;

            font = new TMP_MSDFAFont(data, locations, glyf.Offset, unitsPerEm, numGlyphs, normalizeContourWinding);
            return true;
        }

        internal bool TryGetGlyphShape(uint glyphIndex, out GlyphShape shape)
        {
            if (m_GlyphCache.TryGetValue(glyphIndex, out shape))
                return shape.Segments.Count > 0;

            shape = new GlyphShape();
            if (glyphIndex >= m_NumGlyphs || glyphIndex + 1 >= m_Locations.Length)
            {
                m_GlyphCache[glyphIndex] = shape;
                return false;
            }

            List<List<ContourEdge>> contours = new List<List<ContourEdge>>();
            ParseGlyph(glyphIndex, AffineTransform.Identity, 0, contours);
            if (m_NormalizeContourWinding)
                NormalizeContourWinding(contours);

            EdgeColoringInkTrap(contours, EdgeColoringAngleThreshold, 0);
            AddSegments(contours, shape.Segments, ref shape.Bounds);
            m_GlyphCache[glyphIndex] = shape;
            return shape.Segments.Count > 0;
        }

        private bool ParseGlyph(uint glyphIndex, AffineTransform transform, int depth, List<List<ContourEdge>> contours)
        {
            if (depth > MaxCompositeDepth || glyphIndex >= m_NumGlyphs || glyphIndex + 1 >= m_Locations.Length)
                return false;

            uint glyphOffset = m_Locations[glyphIndex];
            uint nextGlyphOffset = m_Locations[glyphIndex + 1];
            if (glyphOffset == nextGlyphOffset)
                return true;

            int offset = checked((int)(m_GlyphTableOffset + glyphOffset));
            int nextOffset = checked((int)(m_GlyphTableOffset + nextGlyphOffset));
            if (offset + 10 > m_Data.Length || nextOffset > m_Data.Length || offset >= nextOffset)
                return false;

            short numberOfContours = ReadInt16(m_Data, offset);
            if (numberOfContours >= 0)
                return ParseSimpleGlyph(offset, numberOfContours, transform, contours);

            return ParseCompositeGlyph(offset, nextOffset, transform, depth, contours);
        }

        private bool ParseSimpleGlyph(int glyphOffset, short numberOfContours, AffineTransform transform, List<List<ContourEdge>> contours)
        {
            if (numberOfContours == 0)
                return true;

            int cursor = glyphOffset + 10;
            if (cursor + numberOfContours * 2 > m_Data.Length)
                return false;

            ushort[] endPoints = new ushort[numberOfContours];
            for (int i = 0; i < numberOfContours; i++)
            {
                endPoints[i] = ReadUInt16(m_Data, cursor);
                cursor += 2;
            }

            int pointCount = endPoints[numberOfContours - 1] + 1;
            if (pointCount <= 0)
                return true;

            if (cursor + 2 > m_Data.Length)
                return false;

            ushort instructionLength = ReadUInt16(m_Data, cursor);
            cursor += 2 + instructionLength;
            if (cursor > m_Data.Length)
                return false;

            byte[] flags = new byte[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                if (cursor >= m_Data.Length)
                    return false;

                byte flag = m_Data[cursor++];
                flags[i] = flag;

                if ((flag & Repeat) == 0)
                    continue;

                if (cursor >= m_Data.Length)
                    return false;

                byte repeatCount = m_Data[cursor++];
                for (int repeatIndex = 0; repeatIndex < repeatCount && i + 1 < pointCount; repeatIndex++)
                    flags[++i] = flag;
            }

            short[] xCoordinates = new short[pointCount];
            short x = 0;
            for (int i = 0; i < pointCount; i++)
            {
                byte flag = flags[i];
                int delta;
                if ((flag & XShortVector) != 0)
                {
                    if (cursor >= m_Data.Length)
                        return false;

                    delta = m_Data[cursor++];
                    if ((flag & XIsSameOrPositive) == 0)
                        delta = -delta;
                }
                else if ((flag & XIsSameOrPositive) != 0)
                {
                    delta = 0;
                }
                else
                {
                    if (cursor + 2 > m_Data.Length)
                        return false;

                    delta = ReadInt16(m_Data, cursor);
                    cursor += 2;
                }

                x = unchecked((short)(x + delta));
                xCoordinates[i] = x;
            }

            short[] yCoordinates = new short[pointCount];
            short y = 0;
            for (int i = 0; i < pointCount; i++)
            {
                byte flag = flags[i];
                int delta;
                if ((flag & YShortVector) != 0)
                {
                    if (cursor >= m_Data.Length)
                        return false;

                    delta = m_Data[cursor++];
                    if ((flag & YIsSameOrPositive) == 0)
                        delta = -delta;
                }
                else if ((flag & YIsSameOrPositive) != 0)
                {
                    delta = 0;
                }
                else
                {
                    if (cursor + 2 > m_Data.Length)
                        return false;

                    delta = ReadInt16(m_Data, cursor);
                    cursor += 2;
                }

                y = unchecked((short)(y + delta));
                yCoordinates[i] = y;
            }

            int start = 0;
            for (int contourIndex = 0; contourIndex < numberOfContours; contourIndex++)
            {
                int end = endPoints[contourIndex];
                AddContour(xCoordinates, yCoordinates, flags, start, end, transform, contours);
                start = end + 1;
            }

            return true;
        }

        private bool ParseCompositeGlyph(int glyphOffset, int nextGlyphOffset, AffineTransform transform, int depth, List<List<ContourEdge>> contours)
        {
            const ushort Arg1And2AreWords = 0x0001;
            const ushort ArgsAreXyValues = 0x0002;
            const ushort WeHaveAScale = 0x0008;
            const ushort MoreComponents = 0x0020;
            const ushort WeHaveXAndYScale = 0x0040;
            const ushort WeHaveTwoByTwo = 0x0080;
            const ushort WeHaveInstructions = 0x0100;

            int cursor = glyphOffset + 10;
            ushort flags;
            do
            {
                if (cursor + 4 > nextGlyphOffset)
                    return false;

                flags = ReadUInt16(m_Data, cursor);
                cursor += 2;
                ushort componentGlyphIndex = ReadUInt16(m_Data, cursor);
                cursor += 2;

                int argument1;
                int argument2;
                if ((flags & Arg1And2AreWords) != 0)
                {
                    if (cursor + 4 > nextGlyphOffset)
                        return false;

                    argument1 = ReadInt16(m_Data, cursor);
                    argument2 = ReadInt16(m_Data, cursor + 2);
                    cursor += 4;
                }
                else
                {
                    if (cursor + 2 > nextGlyphOffset)
                        return false;

                    argument1 = unchecked((sbyte)m_Data[cursor]);
                    argument2 = unchecked((sbyte)m_Data[cursor + 1]);
                    cursor += 2;
                }

                float dx = 0;
                float dy = 0;
                if ((flags & ArgsAreXyValues) != 0)
                {
                    dx = argument1;
                    dy = argument2;
                }

                AffineTransform componentTransform = new AffineTransform(1, 0, 0, 1, dx, dy);
                if ((flags & WeHaveAScale) != 0)
                {
                    if (cursor + 2 > nextGlyphOffset)
                        return false;

                    float scale = ReadF2Dot14(m_Data, cursor);
                    cursor += 2;
                    componentTransform = new AffineTransform(scale, 0, 0, scale, dx, dy);
                }
                else if ((flags & WeHaveXAndYScale) != 0)
                {
                    if (cursor + 4 > nextGlyphOffset)
                        return false;

                    float scaleX = ReadF2Dot14(m_Data, cursor);
                    float scaleY = ReadF2Dot14(m_Data, cursor + 2);
                    cursor += 4;
                    componentTransform = new AffineTransform(scaleX, 0, 0, scaleY, dx, dy);
                }
                else if ((flags & WeHaveTwoByTwo) != 0)
                {
                    if (cursor + 8 > nextGlyphOffset)
                        return false;

                    float m00 = ReadF2Dot14(m_Data, cursor);
                    float m01 = ReadF2Dot14(m_Data, cursor + 2);
                    float m10 = ReadF2Dot14(m_Data, cursor + 4);
                    float m11 = ReadF2Dot14(m_Data, cursor + 6);
                    cursor += 8;
                    componentTransform = new AffineTransform(m00, m01, m10, m11, dx, dy);
                }

                ParseGlyph(componentGlyphIndex, transform.Combine(componentTransform), depth + 1, contours);
            }
            while ((flags & MoreComponents) != 0);

            if ((flags & WeHaveInstructions) != 0)
            {
                if (cursor + 2 > nextGlyphOffset)
                    return false;

                ushort instructionLength = ReadUInt16(m_Data, cursor);
                cursor += 2 + instructionLength;
                if (cursor > nextGlyphOffset)
                    return false;
            }

            return true;
        }

        private static void AddContour(short[] xCoordinates, short[] yCoordinates, byte[] flags, int start, int end, AffineTransform transform, List<List<ContourEdge>> contours)
        {
            int count = end - start + 1;
            if (count <= 1)
                return;

            Point first = GetPoint(xCoordinates, yCoordinates, flags, start);
            Point last = GetPoint(xCoordinates, yCoordinates, flags, end);
            Vector2 currentRaw = first.OnCurve ? first.Position : last.OnCurve ? last.Position : Vector2.Lerp(last.Position, first.Position, 0.5f);
            Vector2 current = transform.TransformPoint(currentRaw);
            List<ContourEdge> contourEdges = new List<ContourEdge>(count);

            for (int index = start; index <= end; index++)
            {
                Point point = GetPoint(xCoordinates, yCoordinates, flags, index);
                if (point.OnCurve)
                {
                    Vector2 target = transform.TransformPoint(point.Position);
                    AppendContourLine(contourEdges, current, target);
                    current = target;
                    continue;
                }

                int nextIndex = index == end ? start : index + 1;
                Point next = GetPoint(xCoordinates, yCoordinates, flags, nextIndex);
                Vector2 targetRaw = next.OnCurve ? next.Position : Vector2.Lerp(point.Position, next.Position, 0.5f);
                Vector2 control = transform.TransformPoint(point.Position);
                Vector2 curveTarget = transform.TransformPoint(targetRaw);
                AppendContourQuadratic(contourEdges, current, control, curveTarget);
                current = curveTarget;

                if (next.OnCurve && nextIndex != start)
                    index++;
            }

            AppendContourLine(contourEdges, current, transform.TransformPoint(currentRaw));

            if (contourEdges.Count > 0)
                contours.Add(contourEdges);
        }

        private static Point GetPoint(short[] xCoordinates, short[] yCoordinates, byte[] flags, int index)
        {
            return new Point(new Vector2(xCoordinates[index], yCoordinates[index]), (flags[index] & OnCurve) != 0);
        }

        private static void AppendContourLine(List<ContourEdge> contourEdges, Vector2 start, Vector2 end)
        {
            if (Vector2.DistanceSquared(start, end) <= 0.0001f)
                return;

            contourEdges.Add(ContourEdge.CreateLine(start, end));
        }

        private static void AppendContourQuadratic(List<ContourEdge> contourEdges, Vector2 start, Vector2 control, Vector2 end)
        {
            if (Vector2.DistanceSquared(start, end) <= 0.0001f && Vector2.DistanceSquared(start, control) <= 0.0001f)
                return;

            if (Math.Abs(Vector2.Cross(control - start, end - control)) <= 0.0001f)
            {
                AppendContourLine(contourEdges, start, end);
                return;
            }

            contourEdges.Add(ContourEdge.CreateQuadratic(start, control, end));
        }

        private static void NormalizeContourWinding(List<List<ContourEdge>> contours)
        {
            float area = 0;
            for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
                area += SignedArea(contours[contourIndex]);

            if (area <= 0)
                return;

            for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
                ReverseContour(contours[contourIndex]);
        }

        private static float SignedArea(List<ContourEdge> contourEdges)
        {
            float area = 0;
            for (int edgeIndex = 0; edgeIndex < contourEdges.Count; edgeIndex++)
            {
                ContourEdge edge = contourEdges[edgeIndex];
                Vector2 previous = edge.Point(0);
                for (int step = 1; step <= EdgeLengthPrecision; step++)
                {
                    Vector2 current = edge.Point(step / (float)EdgeLengthPrecision);
                    area += previous.X * current.Y - current.X * previous.Y;
                    previous = current;
                }
            }

            return area * 0.5f;
        }

        private static void ReverseContour(List<ContourEdge> contourEdges)
        {
            int count = contourEdges.Count;
            ContourEdge[] reversedEdges = new ContourEdge[count];
            for (int i = 0; i < count; i++)
                reversedEdges[i] = contourEdges[count - i - 1].Reversed();

            contourEdges.Clear();
            contourEdges.AddRange(reversedEdges);
        }

        private static void EdgeColoringInkTrap(List<List<ContourEdge>> contours, float angleThreshold, ulong seed)
        {
            float crossThreshold = (float)Math.Sin(angleThreshold);
            int color = InitColor(ref seed);
            List<Corner> corners = new List<Corner>();
            for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
            {
                List<ContourEdge> contourEdges = contours[contourIndex];
                if (contourEdges.Count == 0)
                    continue;

                float splineLength = 0;
                corners.Clear();
                Vector2 previousDirection = contourEdges[contourEdges.Count - 1].EndDirection;
                for (int i = 0; i < contourEdges.Count; i++)
                {
                    ContourEdge edge = contourEdges[i];
                    if (IsCorner(previousDirection.Normalize(), edge.StartDirection.Normalize(), crossThreshold))
                    {
                        corners.Add(new Corner(i, splineLength));
                        splineLength = 0;
                    }

                    splineLength += EstimateEdgeLength(edge);
                    previousDirection = edge.EndDirection;
                }

                if (corners.Count == 0)
                {
                    color = SwitchColor(color, ref seed);
                    SetContourColor(contourEdges, color);
                }
                else if (corners.Count == 1)
                {
                    ColorTeardropContour(contourEdges, corners[0].Index, ref color, ref seed);
                }
                else
                {
                    ColorMultiCornerContour(contourEdges, corners, splineLength, ref color, ref seed);
                }
            }
        }

        private static bool IsCorner(Vector2 previousDirection, Vector2 currentDirection, float crossThreshold)
        {
            return Vector2.Dot(previousDirection, currentDirection) <= 0 || Math.Abs(Vector2.Cross(previousDirection, currentDirection)) > crossThreshold;
        }

        private static float EstimateEdgeLength(ContourEdge edge)
        {
            float length = 0;
            Vector2 previous = edge.Point(0);
            for (int i = 1; i <= EdgeLengthPrecision; i++)
            {
                Vector2 current = edge.Point(i / (float)EdgeLengthPrecision);
                length += Vector2.Distance(previous, current);
                previous = current;
            }

            return length;
        }

        private static void SetContourColor(List<ContourEdge> contourEdges, int color)
        {
            for (int i = 0; i < contourEdges.Count; i++)
                contourEdges[i].ColorMask = color;
        }

        private static void ColorTeardropContour(List<ContourEdge> contourEdges, int corner, ref int color, ref ulong seed)
        {
            int[] colors = new int[3];
            color = SwitchColor(color, ref seed);
            colors[0] = color;
            colors[1] = White;
            color = SwitchColor(color, ref seed);
            colors[2] = color;

            if (contourEdges.Count >= 3)
            {
                int edgeCount = contourEdges.Count;
                for (int i = 0; i < edgeCount; i++)
                    contourEdges[(corner + i) % edgeCount].ColorMask = colors[1 + SymmetricalTrichotomy(i, edgeCount)];

                return;
            }

            if (contourEdges.Count == 0)
                return;

            ContourEdge[] parts = new ContourEdge[7];
            ContourEdge[] firstParts = contourEdges[0].SplitInThirds();
            parts[0 + 3 * corner] = firstParts[0];
            parts[1 + 3 * corner] = firstParts[1];
            parts[2 + 3 * corner] = firstParts[2];
            if (contourEdges.Count >= 2)
            {
                ContourEdge[] secondParts = contourEdges[1].SplitInThirds();
                parts[3 - 3 * corner] = secondParts[0];
                parts[4 - 3 * corner] = secondParts[1];
                parts[5 - 3 * corner] = secondParts[2];
                parts[0].ColorMask = colors[0];
                parts[1].ColorMask = colors[0];
                parts[2].ColorMask = colors[1];
                parts[3].ColorMask = colors[1];
                parts[4].ColorMask = colors[2];
                parts[5].ColorMask = colors[2];
            }
            else
            {
                parts[0].ColorMask = colors[0];
                parts[1].ColorMask = colors[1];
                parts[2].ColorMask = colors[2];
            }

            contourEdges.Clear();
            for (int i = 0; i < parts.Length && parts[i] != null; i++)
                contourEdges.Add(parts[i]);
        }

        private static void ColorMultiCornerContour(List<ContourEdge> contourEdges, List<Corner> corners, float splineLength, ref int color, ref ulong seed)
        {
            int cornerCount = corners.Count;
            int majorCornerCount = cornerCount;
            if (cornerCount > 3)
            {
                corners[0].PreviousEdgeLengthEstimate += splineLength;
                for (int i = 0; i < cornerCount; i++)
                {
                    if (corners[i].PreviousEdgeLengthEstimate > corners[(i + 1) % cornerCount].PreviousEdgeLengthEstimate
                        && corners[(i + 1) % cornerCount].PreviousEdgeLengthEstimate < corners[(i + 2) % cornerCount].PreviousEdgeLengthEstimate)
                    {
                        corners[i].Minor = true;
                        majorCornerCount--;
                    }
                }
            }

            int initialColor = Black;
            for (int i = 0; i < cornerCount; i++)
            {
                if (corners[i].Minor)
                    continue;

                majorCornerCount--;
                color = SwitchColor(color, ref seed, majorCornerCount == 0 ? initialColor : Black);
                corners[i].ColorMask = color;
                if (initialColor == Black)
                    initialColor = color;
            }

            for (int i = 0; i < cornerCount; i++)
            {
                if (corners[i].Minor)
                {
                    int nextColor = corners[(i + 1) % cornerCount].ColorMask;
                    corners[i].ColorMask = (color & nextColor) ^ White;
                }
                else
                {
                    color = corners[i].ColorMask;
                }
            }

            int spline = 0;
            int start = corners[0].Index;
            color = corners[0].ColorMask;
            int edgeCount = contourEdges.Count;
            for (int i = 0; i < edgeCount; i++)
            {
                int index = (start + i) % edgeCount;
                if (spline + 1 < cornerCount && corners[spline + 1].Index == index)
                    color = corners[++spline].ColorMask;

                contourEdges[index].ColorMask = color;
            }
        }

        private static int SymmetricalTrichotomy(int position, int count)
        {
            return (int)(3 + 2.875f * position / (count - 1) - 1.4375f + 0.5f) - 3;
        }

        private static int InitColor(ref ulong seed)
        {
            switch (SeedExtract3(ref seed))
            {
                case 0:
                    return Cyan;
                case 1:
                    return Magenta;
                default:
                    return Yellow;
            }
        }

        private static int SwitchColor(int color, ref ulong seed)
        {
            int shifted = color << (1 + SeedExtract2(ref seed));
            return (shifted | (shifted >> 3)) & White;
        }

        private static int SwitchColor(int color, ref ulong seed, int banned)
        {
            int combined = color & banned;
            if (combined == Red || combined == Green || combined == Blue)
                return combined ^ White;

            return SwitchColor(color, ref seed);
        }

        private static int SeedExtract2(ref ulong seed)
        {
            int value = (int)seed & 1;
            seed >>= 1;
            return value;
        }

        private static int SeedExtract3(ref ulong seed)
        {
            int value = (int)(seed % 3);
            seed /= 3;
            return value;
        }

        private static void AddSegments(List<List<ContourEdge>> contours, List<Segment> segments, ref Bounds bounds)
        {
            for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
            {
                List<ContourEdge> contourEdges = contours[contourIndex];
                for (int edgeIndex = 0; edgeIndex < contourEdges.Count; edgeIndex++)
                {
                    ContourEdge edge = contourEdges[edgeIndex];
                    edge.IncludeBounds(ref bounds);
                    segments.Add(edge.ToSegment());
                }
            }
        }

        private static bool TryReadTables(byte[] data, out Dictionary<string, TableRecord> tables)
        {
            tables = null;
            if (data.Length < 12)
                return false;

            ushort numTables = ReadUInt16(data, 4);
            int cursor = 12;
            if (cursor + numTables * 16 > data.Length)
                return false;

            tables = new Dictionary<string, TableRecord>(numTables);
            for (int i = 0; i < numTables; i++)
            {
                string tag = System.Text.Encoding.ASCII.GetString(data, cursor, 4);
                uint offset = ReadUInt32(data, cursor + 8);
                uint length = ReadUInt32(data, cursor + 12);
                if (offset + length > data.Length)
                    return false;

                tables[tag] = new TableRecord(offset, length);
                cursor += 16;
            }

            return true;
        }

        private static uint[] ReadLocations(byte[] data, TableRecord loca, ushort numGlyphs, short indexToLocFormat)
        {
            uint[] locations = new uint[numGlyphs + 1];
            if (indexToLocFormat == 0)
            {
                if (loca.Offset + (uint)(locations.Length * 2) > data.Length)
                    return null;

                for (int i = 0; i < locations.Length; i++)
                    locations[i] = (uint)(ReadUInt16(data, loca.Offset + (uint)(i * 2)) * 2);
            }
            else
            {
                if (loca.Offset + (uint)(locations.Length * 4) > data.Length)
                    return null;

                for (int i = 0; i < locations.Length; i++)
                    locations[i] = ReadUInt32(data, checked((int)(loca.Offset + (uint)(i * 4))));
            }

            return locations;
        }

        private static ushort ReadUInt16(byte[] data, uint offset)
        {
            return ReadUInt16(data, checked((int)offset));
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static short ReadInt16(byte[] data, uint offset)
        {
            return ReadInt16(data, checked((int)offset));
        }

        private static short ReadInt16(byte[] data, int offset)
        {
            return unchecked((short)ReadUInt16(data, offset));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static float ReadF2Dot14(byte[] data, int offset)
        {
            return ReadInt16(data, offset) / 16384f;
        }

        internal sealed class GlyphShape
        {
            internal readonly List<Segment> Segments = new List<Segment>();
            internal Bounds Bounds;
        }

        internal struct Segment
        {
            internal int Type;
            internal float X0;
            internal float Y0;
            internal float X1;
            internal float Y1;
            internal float X2;
            internal float Y2;
            internal int ColorMask;

            internal Segment(int type, float x0, float y0, float x1, float y1, float x2, float y2, int colorMask)
            {
                Type = type;
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
                X2 = x2;
                Y2 = y2;
                ColorMask = colorMask;
            }
        }

        internal struct Bounds
        {
            internal bool IsValid;
            internal float MinX;
            internal float MinY;
            internal float MaxX;
            internal float MaxY;

            internal float Width => MaxX - MinX;
            internal float Height => MaxY - MinY;

            internal void Include(Vector2 point)
            {
                if (IsValid == false)
                {
                    IsValid = true;
                    MinX = point.X;
                    MinY = point.Y;
                    MaxX = point.X;
                    MaxY = point.Y;
                    return;
                }

                if (point.X < MinX)
                    MinX = point.X;
                if (point.Y < MinY)
                    MinY = point.Y;
                if (point.X > MaxX)
                    MaxX = point.X;
                if (point.Y > MaxY)
                    MaxY = point.Y;
            }
        }

        private readonly struct TableRecord
        {
            internal readonly uint Offset;
            internal readonly uint Length;

            internal TableRecord(uint offset, uint length)
            {
                Offset = offset;
                Length = length;
            }
        }

        private readonly struct Point
        {
            internal readonly Vector2 Position;
            internal readonly bool OnCurve;

            internal Point(Vector2 position, bool onCurve)
            {
                Position = position;
                OnCurve = onCurve;
            }
        }

        private sealed class Corner
        {
            internal readonly int Index;
            internal float PreviousEdgeLengthEstimate;
            internal bool Minor;
            internal int ColorMask;

            internal Corner(int index, float previousEdgeLengthEstimate)
            {
                Index = index;
                PreviousEdgeLengthEstimate = previousEdgeLengthEstimate;
            }
        }

        private sealed class ContourEdge
        {
            private readonly int m_Type;
            private readonly Vector2 m_Point0;
            private readonly Vector2 m_Point1;
            private readonly Vector2 m_Point2;

            private ContourEdge(int type, Vector2 point0, Vector2 point1, Vector2 point2)
            {
                m_Type = type;
                m_Point0 = point0;
                m_Point1 = point1;
                m_Point2 = point2;
                ColorMask = White;
            }

            internal int ColorMask { get; set; }

            internal Vector2 StartDirection
            {
                get
                {
                    Vector2 direction = m_Point1 - m_Point0;
                    if (direction.IsZero && m_Type == EdgeTypeQuadratic)
                        return m_Point2 - m_Point0;

                    return direction;
                }
            }

            internal Vector2 EndDirection
            {
                get
                {
                    Vector2 direction = m_Type == EdgeTypeQuadratic ? m_Point2 - m_Point1 : m_Point1 - m_Point0;
                    if (direction.IsZero && m_Type == EdgeTypeQuadratic)
                        return m_Point2 - m_Point0;

                    return direction;
                }
            }

            internal static ContourEdge CreateLine(Vector2 start, Vector2 end)
            {
                return new ContourEdge(EdgeTypeLinear, start, end, end);
            }

            internal static ContourEdge CreateQuadratic(Vector2 start, Vector2 control, Vector2 end)
            {
                return new ContourEdge(EdgeTypeQuadratic, start, control, end);
            }

            internal Vector2 Point(float parameter)
            {
                if (m_Type == EdgeTypeLinear)
                    return Vector2.Lerp(m_Point0, m_Point1, parameter);

                Vector2 startControl = Vector2.Lerp(m_Point0, m_Point1, parameter);
                Vector2 controlEnd = Vector2.Lerp(m_Point1, m_Point2, parameter);
                return Vector2.Lerp(startControl, controlEnd, parameter);
            }

            internal ContourEdge[] SplitInThirds()
            {
                if (m_Type == EdgeTypeLinear)
                {
                    return new[]
                    {
                        CreateLine(m_Point0, Point(1f / 3f)),
                        CreateLine(Point(1f / 3f), Point(2f / 3f)),
                        CreateLine(Point(2f / 3f), m_Point1)
                    };
                }

                return new[]
                {
                    CreateQuadratic(m_Point0, Vector2.Lerp(m_Point0, m_Point1, 1f / 3f), Point(1f / 3f)),
                    CreateQuadratic(Point(1f / 3f), Vector2.Lerp(Vector2.Lerp(m_Point0, m_Point1, 5f / 9f), Vector2.Lerp(m_Point1, m_Point2, 4f / 9f), 0.5f), Point(2f / 3f)),
                    CreateQuadratic(Point(2f / 3f), Vector2.Lerp(m_Point1, m_Point2, 2f / 3f), m_Point2)
                };
            }

            internal ContourEdge Reversed()
            {
                if (m_Type == EdgeTypeLinear)
                    return CreateLine(m_Point1, m_Point0);

                return CreateQuadratic(m_Point2, m_Point1, m_Point0);
            }

            internal Segment ToSegment()
            {
                return new Segment(m_Type, m_Point0.X, m_Point0.Y, m_Point1.X, m_Point1.Y, m_Point2.X, m_Point2.Y, ColorMask);
            }

            internal void IncludeBounds(ref Bounds bounds)
            {
                bounds.Include(m_Point0);
                if (m_Type == EdgeTypeLinear)
                {
                    bounds.Include(m_Point1);
                    return;
                }

                bounds.Include(m_Point2);
                Vector2 bottom = (m_Point1 - m_Point0) - (m_Point2 - m_Point1);
                if (Math.Abs(bottom.X) > 0.0001f)
                {
                    float parameter = (m_Point1.X - m_Point0.X) / bottom.X;
                    if (parameter > 0 && parameter < 1)
                        bounds.Include(Point(parameter));
                }

                if (Math.Abs(bottom.Y) > 0.0001f)
                {
                    float parameter = (m_Point1.Y - m_Point0.Y) / bottom.Y;
                    if (parameter > 0 && parameter < 1)
                        bounds.Include(Point(parameter));
                }
            }
        }

        internal readonly struct Vector2
        {
            internal readonly float X;
            internal readonly float Y;

            internal Vector2(float x, float y)
            {
                X = x;
                Y = y;
            }

            internal bool IsZero => X == 0 && Y == 0;

            internal Vector2 Normalize()
            {
                float length = Distance(new Vector2(0, 0), this);
                if (length > 0)
                    return new Vector2(X / length, Y / length);

                return new Vector2(0, 1);
            }

            internal static Vector2 Lerp(Vector2 a, Vector2 b, float t)
            {
                return new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }

            internal static float Distance(Vector2 a, Vector2 b)
            {
                return (float)Math.Sqrt(DistanceSquared(a, b));
            }

            internal static float DistanceSquared(Vector2 a, Vector2 b)
            {
                float dx = a.X - b.X;
                float dy = a.Y - b.Y;
                return dx * dx + dy * dy;
            }

            internal static float Dot(Vector2 a, Vector2 b)
            {
                return a.X * b.X + a.Y * b.Y;
            }

            internal static float Cross(Vector2 a, Vector2 b)
            {
                return a.X * b.Y - a.Y * b.X;
            }

            public static Vector2 operator +(Vector2 a, Vector2 b)
            {
                return new Vector2(a.X + b.X, a.Y + b.Y);
            }

            public static Vector2 operator -(Vector2 a, Vector2 b)
            {
                return new Vector2(a.X - b.X, a.Y - b.Y);
            }

            public static Vector2 operator *(float value, Vector2 vector)
            {
                return new Vector2(value * vector.X, value * vector.Y);
            }
        }

        private readonly struct AffineTransform
        {
            internal static readonly AffineTransform Identity = new AffineTransform(1, 0, 0, 1, 0, 0);

            private readonly float m_00;
            private readonly float m_01;
            private readonly float m_10;
            private readonly float m_11;
            private readonly float m_Dx;
            private readonly float m_Dy;

            internal AffineTransform(float m00, float m01, float m10, float m11, float dx, float dy)
            {
                m_00 = m00;
                m_01 = m01;
                m_10 = m10;
                m_11 = m11;
                m_Dx = dx;
                m_Dy = dy;
            }

            internal Vector2 TransformPoint(Vector2 point)
            {
                return new Vector2(m_00 * point.X + m_01 * point.Y + m_Dx, m_10 * point.X + m_11 * point.Y + m_Dy);
            }

            internal AffineTransform Combine(AffineTransform child)
            {
                return new AffineTransform(
                    m_00 * child.m_00 + m_01 * child.m_10,
                    m_00 * child.m_01 + m_01 * child.m_11,
                    m_10 * child.m_00 + m_11 * child.m_10,
                    m_10 * child.m_01 + m_11 * child.m_11,
                    m_00 * child.m_Dx + m_01 * child.m_Dy + m_Dx,
                    m_10 * child.m_Dx + m_11 * child.m_Dy + m_Dy);
            }
        }
    }
}
#endif
