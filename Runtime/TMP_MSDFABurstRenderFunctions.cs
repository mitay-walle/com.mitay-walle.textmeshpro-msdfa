#if TMP_MSDFA_UGUI_PATCHED
using System;
using Unity.Burst;

namespace TMPro
{
    using MsdfaSegment = TMP_MSDFABurstRenderer.MsdfaSegment;

    [BurstCompile]
    internal static unsafe class TMP_MSDFABurstRenderFunctions
    {
        private const float MaxDistance = 3.40282347e+38f;

        [BurstCompile(CompileSynchronously = true)]
        [AOT.MonoPInvokeCallback(typeof(TMP_MSDFABurstRenderer.CopyGlyphToMsdfaAtlasDelegate))]
        internal static void CopyGlyphToMsdfaAtlasBurst(byte* sourcePixels, byte* atlasPixels, int textureWidth, int textureHeight, int padding, int glyphX, int glyphY, int glyphWidth, int glyphHeight)
        {
            int xMin = glyphX - padding;
            if (xMin < 0)
                xMin = 0;

            int yMin = glyphY - padding;
            if (yMin < 0)
                yMin = 0;

            int xMax = glyphX + glyphWidth + padding;
            if (xMax > textureWidth)
                xMax = textureWidth;

            int yMax = glyphY + glyphHeight + padding;
            if (yMax > textureHeight)
                yMax = textureHeight;

            for (int y = yMin; y < yMax; y++)
            {
                int rowOffset = y * textureWidth;
                for (int x = xMin; x < xMax; x++)
                {
                    int sourceIndex = rowOffset + x;
                    int atlasIndex = sourceIndex * 4;
                    byte distance = sourcePixels[sourceIndex];
                    atlasPixels[atlasIndex] = distance;
                    atlasPixels[atlasIndex + 1] = distance;
                    atlasPixels[atlasIndex + 2] = distance;
                    atlasPixels[atlasIndex + 3] = distance;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        [AOT.MonoPInvokeCallback(typeof(TMP_MSDFABurstRenderer.RenderGlyphMsdfaDelegate))]
        internal static void RenderGlyphMsdfaBurst(MsdfaSegment* segments, int segmentCount, byte* atlasPixels, int textureWidth, int textureHeight, int glyphX, int glyphY, int glyphWidth, int glyphHeight, int padding, float originX, float originY, float boundsMinX, float boundsMinY, float unitScale, float pixelRange, byte* correctionMask)
        {
            int xMin = glyphX - padding;
            if (xMin < 0)
                xMin = 0;

            int yMin = glyphY - padding;
            if (yMin < 0)
                yMin = 0;

            int xMax = glyphX + glyphWidth + padding;
            if (xMax > textureWidth)
                xMax = textureWidth;

            int yMax = glyphY + glyphHeight + padding;
            if (yMax > textureHeight)
                yMax = textureHeight;

            for (int y = yMin; y < yMax; y++)
            {
                for (int x = xMin; x < xMax; x++)
                {
                    float fontX = boundsMinX + (x + 0.5f - originX) / unitScale;
                    float fontY = boundsMinY + (y + 0.5f - originY) / unitScale;
                    float alphaDistance = -MaxDistance;
                    float alphaDot = 0;
                    float redSignedDistance = -MaxDistance;
                    float redDot = 0;
                    float redParam = 0;
                    int redEdgeIndex = -1;
                    float greenSignedDistance = -MaxDistance;
                    float greenDot = 0;
                    float greenParam = 0;
                    int greenEdgeIndex = -1;
                    float blueSignedDistance = -MaxDistance;
                    float blueDot = 0;
                    float blueParam = 0;
                    int blueEdgeIndex = -1;

                    for (int i = 0; i < segmentCount; i++)
                    {
                        MsdfaSegment segment = segments[i];
                        SignedDistanceToMsdfSegment(fontX, fontY, segment, out float signedDistance, out float dot, out float param);
                        if (SignedDistanceLess(signedDistance, dot, alphaDistance, alphaDot))
                        {
                            alphaDistance = signedDistance;
                            alphaDot = dot;
                        }

                        if ((segment.ColorMask & 1) != 0 && SignedDistanceLess(signedDistance, dot, redSignedDistance, redDot))
                        {
                            redSignedDistance = signedDistance;
                            redDot = dot;
                            redParam = param;
                            redEdgeIndex = i;
                        }

                        if ((segment.ColorMask & 2) != 0 && SignedDistanceLess(signedDistance, dot, greenSignedDistance, greenDot))
                        {
                            greenSignedDistance = signedDistance;
                            greenDot = dot;
                            greenParam = param;
                            greenEdgeIndex = i;
                        }

                        if ((segment.ColorMask & 4) != 0 && SignedDistanceLess(signedDistance, dot, blueSignedDistance, blueDot))
                        {
                            blueSignedDistance = signedDistance;
                            blueDot = dot;
                            blueParam = param;
                            blueEdgeIndex = i;
                        }
                    }

                    if (redEdgeIndex >= 0)
                        DistanceToPerpendicularDistance(ref redSignedDistance, fontX, fontY, redParam, segments[redEdgeIndex]);
                    if (greenEdgeIndex >= 0)
                        DistanceToPerpendicularDistance(ref greenSignedDistance, fontX, fontY, greenParam, segments[greenEdgeIndex]);
                    if (blueEdgeIndex >= 0)
                        DistanceToPerpendicularDistance(ref blueSignedDistance, fontX, fontY, blueParam, segments[blueEdgeIndex]);

                    int atlasIndex = (y * textureWidth + x) * 4;
                    atlasPixels[atlasIndex] = EncodeDistance(redSignedDistance * unitScale, pixelRange);
                    atlasPixels[atlasIndex + 1] = EncodeDistance(greenSignedDistance * unitScale, pixelRange);
                    atlasPixels[atlasIndex + 2] = EncodeDistance(blueSignedDistance * unitScale, pixelRange);
                    atlasPixels[atlasIndex + 3] = EncodeDistance(alphaDistance * unitScale, pixelRange);
                }
            }

            CorrectMsdfErrors(atlasPixels, textureWidth, xMin, yMin, xMax, yMax, correctionMask, pixelRange);
        }

        private static void CorrectMsdfErrors(byte* atlasPixels, int textureWidth, int xMin, int yMin, int xMax, int yMax, byte* correctionMask, float pixelRange)
        {
            int width = xMax - xMin;
            int height = yMax - yMin;
            if (width <= 0 || height <= 0 || correctionMask == null)
                return;

            float linearSpan = 255f * 1.11111111111111111f / pixelRange;
            float diagonalSpan = linearSpan * 1.41421356237f;
            ClearMask(correctionMask, width * height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int atlasIndex = ((yMin + y) * textureWidth + xMin + x) * 4;
                    byte* current = atlasPixels + atlasIndex;
                    float currentMedian = Median(current[0], current[1], current[2]);
                    byte* left = x > 0 ? current - 4 : null;
                    byte* bottom = y > 0 ? current - textureWidth * 4 : null;
                    byte* right = x < width - 1 ? current + 4 : null;
                    byte* top = y < height - 1 ? current + textureWidth * 4 : null;
                    if ((left != null && HasLinearArtifact(currentMedian, current, left, linearSpan))
                        || (bottom != null && HasLinearArtifact(currentMedian, current, bottom, linearSpan))
                        || (right != null && HasLinearArtifact(currentMedian, current, right, linearSpan))
                        || (top != null && HasLinearArtifact(currentMedian, current, top, linearSpan))
                        || (left != null && bottom != null && HasDiagonalArtifact(currentMedian, current, left, bottom, bottom - 4, diagonalSpan))
                        || (right != null && bottom != null && HasDiagonalArtifact(currentMedian, current, right, bottom, bottom + 4, diagonalSpan))
                        || (left != null && top != null && HasDiagonalArtifact(currentMedian, current, left, top, top - 4, diagonalSpan))
                        || (right != null && top != null && HasDiagonalArtifact(currentMedian, current, right, top, top + 4, diagonalSpan)))
                        correctionMask[y * width + x] = 1;
                }
            }

            ApplyCorrectionMask(atlasPixels, textureWidth, xMin, yMin, width, height, correctionMask);
        }

        private static void ClearMask(byte* correctionMask, int length)
        {
            for (int i = 0; i < length; i++)
                correctionMask[i] = 0;
        }

        private static void ApplyCorrectionMask(byte* atlasPixels, int textureWidth, int xMin, int yMin, int width, int height, byte* correctionMask)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (correctionMask[y * width + x] == 0)
                        continue;

                    int atlasIndex = ((yMin + y) * textureWidth + xMin + x) * 4;
                    byte median = Median(atlasPixels[atlasIndex], atlasPixels[atlasIndex + 1], atlasPixels[atlasIndex + 2]);
                    atlasPixels[atlasIndex] = median;
                    atlasPixels[atlasIndex + 1] = median;
                    atlasPixels[atlasIndex + 2] = median;
                }
            }
        }

        private static bool HasLinearArtifact(float currentMedian, byte* current, byte* other, float span)
        {
            float otherMedian = Median(other[0], other[1], other[2]);
            return Abs(currentMedian - 127.5f) >= Abs(otherMedian - 127.5f)
                   && (HasLinearArtifactInner(currentMedian, otherMedian, current, other, current[1] - current[0], other[1] - other[0], span)
                       || HasLinearArtifactInner(currentMedian, otherMedian, current, other, current[2] - current[1], other[2] - other[1], span)
                       || HasLinearArtifactInner(currentMedian, otherMedian, current, other, current[0] - current[2], other[0] - other[2], span));
        }

        private static bool HasLinearArtifactInner(float currentMedian, float otherMedian, byte* current, byte* other, float currentDifference, float otherDifference, float span)
        {
            float denominator = currentDifference - otherDifference;
            if (denominator == 0)
                return false;

            float ratio = currentDifference / denominator;
            if (ratio <= 0.01f || ratio >= 0.99f)
                return false;

            float interpolatedMedian = InterpolatedMedian(current, other, ratio);
            return RangeTest(0, 1, ratio, currentMedian, otherMedian, interpolatedMedian, span);
        }

        private static bool HasDiagonalArtifact(float currentMedian, byte* current, byte* adjacentA, byte* adjacentB, byte* diagonal, float span)
        {
            float diagonalMedian = Median(diagonal[0], diagonal[1], diagonal[2]);
            if (Abs(currentMedian - 127.5f) < Abs(diagonalMedian - 127.5f))
                return false;

            float a0 = current[0] - adjacentA[0] - adjacentB[0];
            float a1 = current[1] - adjacentA[1] - adjacentB[1];
            float a2 = current[2] - adjacentA[2] - adjacentB[2];
            float l0 = -current[0] - a0;
            float l1 = -current[1] - a1;
            float l2 = -current[2] - a2;
            float q0 = diagonal[0] + a0;
            float q1 = diagonal[1] + a1;
            float q2 = diagonal[2] + a2;
            float t0 = q0 == 0 ? -1 : -0.5f * l0 / q0;
            float t1 = q1 == 0 ? -1 : -0.5f * l1 / q1;
            float t2 = q2 == 0 ? -1 : -0.5f * l2 / q2;
            return HasDiagonalArtifactInner(currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, current[1] - current[0], adjacentA[1] - adjacentA[0] + adjacentB[1] - adjacentB[0], diagonal[1] - diagonal[0], t0, t1, span)
                   || HasDiagonalArtifactInner(currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, current[2] - current[1], adjacentA[2] - adjacentA[1] + adjacentB[2] - adjacentB[1], diagonal[2] - diagonal[1], t1, t2, span)
                   || HasDiagonalArtifactInner(currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, current[0] - current[2], adjacentA[0] - adjacentA[2] + adjacentB[0] - adjacentB[2], diagonal[0] - diagonal[2], t2, t0, span);
        }

        private static bool HasDiagonalArtifactInner(float currentMedian, float diagonalMedian, byte* current, float l0, float l1, float l2, float q0, float q1, float q2, float currentDifference, float adjacentDifference, float diagonalDifference, float tExtremeA, float tExtremeB, float span)
        {
            int solutions = SolveQuadratic(diagonalDifference - adjacentDifference + currentDifference, adjacentDifference - currentDifference - currentDifference, currentDifference, out float t0, out float t1);
            if (HasDiagonalArtifactAtRatio(t0, solutions > 0, currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, tExtremeA, tExtremeB, span))
                return true;

            return HasDiagonalArtifactAtRatio(t1, solutions > 1, currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, tExtremeA, tExtremeB, span);
        }

        private static bool HasDiagonalArtifactAtRatio(float ratio, bool valid, float currentMedian, float diagonalMedian, byte* current, float l0, float l1, float l2, float q0, float q1, float q2, float tExtremeA, float tExtremeB, float span)
        {
            if (valid == false || ratio <= 0.01f || ratio >= 0.99f)
                return false;

            float interpolatedMedian = InterpolatedMedian(current, l0, l1, l2, q0, q1, q2, ratio);
            if (RangeTest(0, 1, ratio, currentMedian, diagonalMedian, interpolatedMedian, span))
                return true;

            if (tExtremeA > 0 && tExtremeA < 1 && RangeTestWithExtreme(ratio, tExtremeA, currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, interpolatedMedian, span))
                return true;

            return tExtremeB > 0 && tExtremeB < 1 && RangeTestWithExtreme(ratio, tExtremeB, currentMedian, diagonalMedian, current, l0, l1, l2, q0, q1, q2, interpolatedMedian, span);
        }

        private static bool RangeTestWithExtreme(float ratio, float extremeRatio, float currentMedian, float diagonalMedian, byte* current, float l0, float l1, float l2, float q0, float q1, float q2, float interpolatedMedian, float span)
        {
            float startRatio = 0;
            float endRatio = 1;
            float startMedian = currentMedian;
            float endMedian = diagonalMedian;
            if (extremeRatio > ratio)
            {
                endRatio = extremeRatio;
                endMedian = InterpolatedMedian(current, l0, l1, l2, q0, q1, q2, extremeRatio);
            }
            else
            {
                startRatio = extremeRatio;
                startMedian = InterpolatedMedian(current, l0, l1, l2, q0, q1, q2, extremeRatio);
            }

            return RangeTest(startRatio, endRatio, ratio, startMedian, endMedian, interpolatedMedian, span);
        }

        private static bool RangeTest(float currentRatio, float otherRatio, float interpolationRatio, float currentMedian, float otherMedian, float interpolatedMedian, float span)
        {
            if (((currentMedian > 127.5f && otherMedian > 127.5f && interpolatedMedian <= 127.5f)
                 || (currentMedian < 127.5f && otherMedian < 127.5f && interpolatedMedian >= 127.5f)
                 || Median(currentMedian, otherMedian, interpolatedMedian) != interpolatedMedian) == false)
                return false;

            float currentSpan = (interpolationRatio - currentRatio) * span;
            float otherSpan = (otherRatio - interpolationRatio) * span;
            return interpolatedMedian < currentMedian - currentSpan
                   || interpolatedMedian > currentMedian + currentSpan
                   || interpolatedMedian < otherMedian - otherSpan
                   || interpolatedMedian > otherMedian + otherSpan;
        }

        private static float InterpolatedMedian(byte* current, byte* other, float ratio)
        {
            return Median(
                Mix(current[0], other[0], ratio),
                Mix(current[1], other[1], ratio),
                Mix(current[2], other[2], ratio));
        }

        private static float InterpolatedMedian(byte* current, float l0, float l1, float l2, float q0, float q1, float q2, float ratio)
        {
            return Median(
                ratio * (ratio * q0 + l0) + current[0],
                ratio * (ratio * q1 + l1) + current[1],
                ratio * (ratio * q2 + l2) + current[2]);
        }

        private static float Mix(float a, float b, float ratio)
        {
            return a + (b - a) * ratio;
        }

        private static byte Median(byte a, byte b, byte c)
        {
            if (a > b)
                Swap(ref a, ref b);
            if (b > c)
                Swap(ref b, ref c);
            if (a > b)
                Swap(ref a, ref b);

            return b;
        }

        private static float Median(float a, float b, float c)
        {
            if (a > b)
                Swap(ref a, ref b);
            if (b > c)
                Swap(ref b, ref c);
            if (a > b)
                Swap(ref a, ref b);

            return b;
        }

        private static void Swap(ref byte a, ref byte b)
        {
            byte value = a;
            a = b;
            b = value;
        }

        private static void Swap(ref float a, ref float b)
        {
            float value = a;
            a = b;
            b = value;
        }

        private static void SignedDistanceToMsdfSegment(float x, float y, MsdfaSegment segment, out float distance, out float dot, out float param)
        {
            if (segment.Type == 2)
            {
                SignedDistanceToQuadraticSegment(x, y, segment, out distance, out dot, out param);
                return;
            }

            SignedDistanceToLinearSegment(x, y, segment, out distance, out dot, out param);
        }

        private static void SignedDistanceToLinearSegment(float x, float y, MsdfaSegment segment, out float distance, out float dot, out float param)
        {
            float aqX = x - segment.X0;
            float aqY = y - segment.Y0;
            float abX = segment.X1 - segment.X0;
            float abY = segment.Y1 - segment.Y0;
            float abLengthSquared = abX * abX + abY * abY;
            if (abLengthSquared <= 0.0001f)
            {
                distance = MaxDistance;
                dot = 0;
                param = 0;
                return;
            }

            param = Dot(aqX, aqY, abX, abY) / abLengthSquared;
            float endpointX = param > 0.5f ? segment.X1 - x : segment.X0 - x;
            float endpointY = param > 0.5f ? segment.Y1 - y : segment.Y0 - y;
            float endpointDistance = Length(endpointX, endpointY);
            if (param > 0 && param < 1)
            {
                float abLength = (float)Math.Sqrt(abLengthSquared);
                float orthoDistance = (abY * aqX - abX * aqY) / abLength;
                if (Abs(orthoDistance) < endpointDistance)
                {
                    distance = orthoDistance;
                    dot = 0;
                    return;
                }
            }

            Normalize(abX, abY, false, out float abNormalX, out float abNormalY);
            Normalize(endpointX, endpointY, false, out float endpointNormalX, out float endpointNormalY);
            distance = NonZeroSign(Cross(aqX, aqY, abX, abY)) * endpointDistance;
            dot = Abs(Dot(abNormalX, abNormalY, endpointNormalX, endpointNormalY));
        }

        private static void SignedDistanceToQuadraticSegment(float x, float y, MsdfaSegment segment, out float distance, out float dot, out float param)
        {
            float qaX = segment.X0 - x;
            float qaY = segment.Y0 - y;
            float abX = segment.X1 - segment.X0;
            float abY = segment.Y1 - segment.Y0;
            float brX = segment.X2 - segment.X1 - abX;
            float brY = segment.Y2 - segment.Y1 - abY;
            float a = Dot(brX, brY, brX, brY);
            float b = 3f * Dot(abX, abY, brX, brY);
            float c = 2f * Dot(abX, abY, abX, abY) + Dot(qaX, qaY, brX, brY);
            float d = Dot(qaX, qaY, abX, abY);
            int solutions = SolveCubic(a, b, c, d, out float t0, out float t1, out float t2);

            Direction(segment, 0, out float endpointDirectionX, out float endpointDirectionY);
            float minDistance = NonZeroSign(Cross(endpointDirectionX, endpointDirectionY, qaX, qaY)) * Length(qaX, qaY);
            float endpointDirectionLengthSquared = Dot(endpointDirectionX, endpointDirectionY, endpointDirectionX, endpointDirectionY);
            param = endpointDirectionLengthSquared <= 0.0001f ? 0 : -Dot(qaX, qaY, endpointDirectionX, endpointDirectionY) / endpointDirectionLengthSquared;

            float endDistanceX = segment.X2 - x;
            float endDistanceY = segment.Y2 - y;
            float endDistance = Length(endDistanceX, endDistanceY);
            if (endDistance < Abs(minDistance))
            {
                Direction(segment, 1, out endpointDirectionX, out endpointDirectionY);
                minDistance = NonZeroSign(Cross(endpointDirectionX, endpointDirectionY, endDistanceX, endDistanceY)) * endDistance;
                endpointDirectionLengthSquared = Dot(endpointDirectionX, endpointDirectionY, endpointDirectionX, endpointDirectionY);
                param = endpointDirectionLengthSquared <= 0.0001f ? 1 : Dot(x - segment.X1, y - segment.Y1, endpointDirectionX, endpointDirectionY) / endpointDirectionLengthSquared;
            }

            ApplyQuadraticRoot(t0, solutions > 0, qaX, qaY, abX, abY, brX, brY, ref minDistance, ref param);
            ApplyQuadraticRoot(t1, solutions > 1, qaX, qaY, abX, abY, brX, brY, ref minDistance, ref param);
            ApplyQuadraticRoot(t2, solutions > 2, qaX, qaY, abX, abY, brX, brY, ref minDistance, ref param);

            distance = minDistance;
            if (param >= 0 && param <= 1)
            {
                dot = 0;
                return;
            }

            if (param < 0.5f)
            {
                Direction(segment, 0, out float directionX, out float directionY);
                Normalize(directionX, directionY, false, out float normalX, out float normalY);
                Normalize(qaX, qaY, false, out float qaNormalX, out float qaNormalY);
                dot = Abs(Dot(normalX, normalY, qaNormalX, qaNormalY));
            }
            else
            {
                Direction(segment, 1, out float directionX, out float directionY);
                Normalize(directionX, directionY, false, out float normalX, out float normalY);
                Normalize(endDistanceX, endDistanceY, false, out float endNormalX, out float endNormalY);
                dot = Abs(Dot(normalX, normalY, endNormalX, endNormalY));
            }
        }

        private static void ApplyQuadraticRoot(float t, bool isValidRoot, float qaX, float qaY, float abX, float abY, float brX, float brY, ref float minDistance, ref float param)
        {
            if (isValidRoot == false || t <= 0 || t >= 1)
                return;

            float qeX = qaX + 2f * t * abX + t * t * brX;
            float qeY = qaY + 2f * t * abY + t * t * brY;
            float distance = Length(qeX, qeY);
            if (distance > Abs(minDistance))
                return;

            minDistance = NonZeroSign(Cross(abX + t * brX, abY + t * brY, qeX, qeY)) * distance;
            param = t;
        }

        private static void DistanceToPerpendicularDistance(ref float distance, float x, float y, float param, MsdfaSegment segment)
        {
            if (param < 0)
            {
                Direction(segment, 0, out float directionX, out float directionY);
                Normalize(directionX, directionY, false, out directionX, out directionY);
                Point(segment, 0, out float pointX, out float pointY);
                float aqX = x - pointX;
                float aqY = y - pointY;
                if (Dot(aqX, aqY, directionX, directionY) < 0)
                {
                    float perpendicularDistance = Cross(aqX, aqY, directionX, directionY);
                    if (Abs(perpendicularDistance) <= Abs(distance))
                        distance = perpendicularDistance;
                }
            }
            else if (param > 1)
            {
                Direction(segment, 1, out float directionX, out float directionY);
                Normalize(directionX, directionY, false, out directionX, out directionY);
                Point(segment, 1, out float pointX, out float pointY);
                float bqX = x - pointX;
                float bqY = y - pointY;
                if (Dot(bqX, bqY, directionX, directionY) > 0)
                {
                    float perpendicularDistance = Cross(bqX, bqY, directionX, directionY);
                    if (Abs(perpendicularDistance) <= Abs(distance))
                        distance = perpendicularDistance;
                }
            }
        }

        private static bool SignedDistanceLess(float distance, float dot, float otherDistance, float otherDot)
        {
            float absoluteDistance = Abs(distance);
            float otherAbsoluteDistance = Abs(otherDistance);
            return absoluteDistance < otherAbsoluteDistance || (absoluteDistance == otherAbsoluteDistance && dot < otherDot);
        }

        private static int SolveQuadratic(float a, float b, float c, out float x0, out float x1)
        {
            x0 = 0;
            x1 = 0;
            if (a == 0 || Abs(b) > 1e12f * Abs(a))
            {
                if (b == 0)
                    return c == 0 ? -1 : 0;

                x0 = -c / b;
                return 1;
            }

            float discriminant = b * b - 4f * a * c;
            if (discriminant > 0)
            {
                discriminant = (float)Math.Sqrt(discriminant);
                x0 = (-b + discriminant) / (2f * a);
                x1 = (-b - discriminant) / (2f * a);
                return 2;
            }

            if (discriminant == 0)
            {
                x0 = -b / (2f * a);
                return 1;
            }

            return 0;
        }

        private static int SolveCubic(float a, float b, float c, float d, out float x0, out float x1, out float x2)
        {
            x0 = 0;
            x1 = 0;
            x2 = 0;
            if (a != 0)
            {
                float normalizedB = b / a;
                if (Abs(normalizedB) < 1e6f)
                    return SolveCubicNormed(normalizedB, c / a, d / a, out x0, out x1, out x2);
            }

            return SolveQuadratic(b, c, d, out x0, out x1);
        }

        private static int SolveCubicNormed(float a, float b, float c, out float x0, out float x1, out float x2)
        {
            x0 = 0;
            x1 = 0;
            x2 = 0;
            float a2 = a * a;
            float q = (a2 - 3f * b) / 9f;
            float r = (a * (2f * a2 - 9f * b) + 27f * c) / 54f;
            float r2 = r * r;
            float q3 = q * q * q;
            a *= 1f / 3f;
            if (r2 < q3)
            {
                float t = r / (float)Math.Sqrt(q3);
                if (t < -1)
                    t = -1;
                else if (t > 1)
                    t = 1;

                t = (float)Math.Acos(t);
                q = -2f * (float)Math.Sqrt(q);
                const float TwoPi = 6.283185307179586476925286766559f;
                x0 = q * (float)Math.Cos(t / 3f) - a;
                x1 = q * (float)Math.Cos((t + TwoPi) / 3f) - a;
                x2 = q * (float)Math.Cos((t - TwoPi) / 3f) - a;
                return 3;
            }

            float u = (r < 0 ? 1 : -1) * (float)Math.Pow(Abs(r) + (float)Math.Sqrt(r2 - q3), 1f / 3f);
            float v = u == 0 ? 0 : q / u;
            x0 = u + v - a;
            if (u == v || Abs(u - v) < 1e-12f * Abs(u + v))
            {
                x1 = -0.5f * (u + v) - a;
                return 2;
            }

            return 1;
        }

        private static void Point(MsdfaSegment segment, float param, out float x, out float y)
        {
            if (segment.Type != 2)
            {
                x = segment.X0 + (segment.X1 - segment.X0) * param;
                y = segment.Y0 + (segment.Y1 - segment.Y0) * param;
                return;
            }

            float inverseParam = 1f - param;
            x = inverseParam * inverseParam * segment.X0 + 2f * inverseParam * param * segment.X1 + param * param * segment.X2;
            y = inverseParam * inverseParam * segment.Y0 + 2f * inverseParam * param * segment.Y1 + param * param * segment.Y2;
        }

        private static void Direction(MsdfaSegment segment, float param, out float x, out float y)
        {
            if (segment.Type != 2)
            {
                x = segment.X1 - segment.X0;
                y = segment.Y1 - segment.Y0;
                return;
            }

            x = (1f - param) * (segment.X1 - segment.X0) + param * (segment.X2 - segment.X1);
            y = (1f - param) * (segment.Y1 - segment.Y0) + param * (segment.Y2 - segment.Y1);
            if (x == 0 && y == 0)
            {
                x = segment.X2 - segment.X0;
                y = segment.Y2 - segment.Y0;
            }
        }

        private static float Abs(float value)
        {
            return value < 0 ? -value : value;
        }

        private static float Length(float x, float y)
        {
            return (float)Math.Sqrt(x * x + y * y);
        }

        private static float Dot(float ax, float ay, float bx, float by)
        {
            return ax * bx + ay * by;
        }

        private static float Cross(float ax, float ay, float bx, float by)
        {
            return ax * by - ay * bx;
        }

        private static int NonZeroSign(float value)
        {
            return value > 0 ? 1 : -1;
        }

        private static void Normalize(float x, float y, bool allowZero, out float normalizedX, out float normalizedY)
        {
            float length = Length(x, y);
            if (length > 0)
            {
                normalizedX = x / length;
                normalizedY = y / length;
                return;
            }

            normalizedX = 0;
            normalizedY = allowZero ? 0 : 1;
        }

        private static byte EncodeDistance(float signedDistance, float pixelRange)
        {
            float value = 0.5f + signedDistance / pixelRange;
            if (value < 0)
                value = 0;
            else if (value > 1)
                value = 1;

            return (byte)(value * 255f + 0.5f);
        }
    }
}
#endif
