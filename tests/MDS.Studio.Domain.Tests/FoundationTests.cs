using MDS.SharedKernel;
using MDS.Studio.Domain;

namespace MDS.Studio.Domain.Tests;
public sealed class FoundationTests
{
 [Fact] public void Length_converts_units_exactly() { Assert.Equal(80m, Length.FromCentimeters(8).Millimeters); Assert.Equal(1180m, Length.FromMeters(1.18m).Millimeters); Assert.Equal(10m, Length.FromMillimeters(100).Centimeters); }
 [Fact] public void File_hash_is_sha256_and_stable() { Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileHash.FromBytes("abc"u8.ToArray()).Value); }
 [Fact] public void Result_failure_contains_error() { var result = Result.Failure(new Error("MDS-STUDIO-API-001", "Expected")); Assert.False(result.IsSuccess); Assert.Equal("MDS-STUDIO-API-001", result.Error.Code); }
 [Fact] public void Job_only_accepts_valid_transitions() { var job = new BackgroundJob { Type = "AgentPingJob", CorrelationId = Guid.NewGuid().ToString() }; Assert.True(job.TransitionTo(JobStatus.Running).IsSuccess); Assert.True(job.TransitionTo(JobStatus.Completed).IsSuccess); Assert.False(job.TransitionTo(JobStatus.Running).IsSuccess); }
}
