using System;
using UnityEngine;

public sealed class SpatialHash3D
{
    private int _capacity;
    private int _tableSize;
    private int _queryStamp;

    private int[] _cellStart;
    private int[] _cellEntries;
    private int[] _objectHashes;
    private int[] _visitedBuckets;
    private Vector3[] _positions;


    public int[] QueryIds { get; private set; }
    public int QueryCount { get; private set; }
    public int ObjectCount { get; private set; }
    public float CellSize { get; private set; } = 1f;

    public SpatialHash3D(int initialCapacity = 128)
    {
        EnsureCapacity(Mathf.Max(1, initialCapacity));
    }

    private void EnsureCapacity(int requiredCount)
    {
        if (requiredCount <= _capacity) return;
        
        _capacity = Mathf.NextPowerOfTwo(Mathf.Max(16, requiredCount));
        _tableSize = 2 * _capacity + 1;

        _cellStart = new int[_tableSize + 1];
        _cellEntries = new int[_capacity];
        _objectHashes = new int[_capacity];
        _visitedBuckets = new int[_tableSize];
        _positions = new Vector3[_capacity];
        QueryIds = new int[_capacity];
        _queryStamp = 0;
    }

    private int IntCoord(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / CellSize);
    }

    private int HashCoords(int x, int y, int z)
    {
        unchecked
        {
            uint hash = ((uint)x * 92837111u) ^ ((uint)y * 689287499u) ^ ((uint)z * 283923481u);

            return (int)(hash % (uint)_tableSize);
        }
    }

    public void Build(Vector3[] positions, int count, float cellSize)
    {
        if (positions == null) throw new ArgumentNullException(nameof(positions));

        if (count < 0 || count > positions.Length) throw new ArgumentOutOfRangeException(nameof(count));

        EnsureCapacity(count);
        ObjectCount = count;
        CellSize = cellSize;
        QueryCount = 0;

        Array.Copy(positions, _positions, count);
        Array.Clear(_cellStart, 0, _cellStart.Length);

  
        for (int i = 0; i < count; i++)
        {
            Vector3 position = _positions[i];
            int hash = HashCoords(IntCoord(position.x), IntCoord(position.y), IntCoord(position.z));

            _objectHashes[i] = hash;
            _cellStart[hash]++;
        }

      
        int start = 0;
        for (int i = 0; i < _tableSize; i++)
        {
            start += _cellStart[i];
            _cellStart[i] = start;
        }
        _cellStart[_tableSize] = start;

 
        for (int i = 0; i < count; i++)
        {
            int hash = _objectHashes[i];
            _cellStart[hash]--;
            _cellEntries[_cellStart[hash]] = i;
        }
    }

    public int Query(int objectIndex, float maxDistance)
    {
        if (objectIndex < 0 || objectIndex >= ObjectCount) throw new ArgumentOutOfRangeException(nameof(objectIndex));

        maxDistance = Mathf.Max(0f, maxDistance);
        Vector3 position = _positions[objectIndex];

        int minX = IntCoord(position.x - maxDistance);
        int minY = IntCoord(position.y - maxDistance);
        int minZ = IntCoord(position.z - maxDistance);
        int maxX = IntCoord(position.x + maxDistance);
        int maxY = IntCoord(position.y + maxDistance);
        int maxZ = IntCoord(position.z + maxDistance);

        QueryCount = 0;

        if (_queryStamp == int.MaxValue)
        {
            Array.Clear(_visitedBuckets, 0, _visitedBuckets.Length);
            _queryStamp = 0;
        }
        _queryStamp++;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    int hash = HashCoords(x, y, z);
                    if (_visitedBuckets[hash] == _queryStamp)
                        continue;

                    _visitedBuckets[hash] = _queryStamp;

                    int start = _cellStart[hash];
                    int end = _cellStart[hash + 1];
                    for (int entry = start; entry < end; entry++)
                    {
                        int id = _cellEntries[entry];
                        Vector3 delta = _positions[id] - position;
                        
                        if (Mathf.Abs(delta.x) > maxDistance
                            || Mathf.Abs(delta.y) > maxDistance
                            || Mathf.Abs(delta.z) > maxDistance)
                            continue;

                        QueryIds[QueryCount++] = id;
                    }
                }
            }
        }

        Array.Sort(QueryIds, 0, QueryCount);
        return QueryCount;
    }
}
