// 이번 판에서 시작 안내부터 완료 또는 중단까지 잰 Wave별 시간이다.
public readonly struct TrainingWaveTimeRecord
{
    public int WaveNumber { get; }
    public float TimeSeconds { get; }
    public bool Cleared { get; }

    public TrainingWaveTimeRecord(int waveNumber, float timeSeconds, bool cleared)
    {
        WaveNumber = waveNumber;
        TimeSeconds = timeSeconds;
        Cleared = cleared;
    }
}
