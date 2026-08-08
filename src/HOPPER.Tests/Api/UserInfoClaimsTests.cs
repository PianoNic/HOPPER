using HOPPER.API.Auth;

namespace HOPPER.Tests.Api
{
    public class UserInfoClaimsTests
    {
        [Test]
        public async Task AGroupsArray_BecomesOneClaimPerGroup()
        {
            // The shape Pocket ID returns, and the one a role check has to match a single entry of.
            var claims = UserInfoClaims.Parse("""
                {"sub":"u1","groups":["private","friends"]}
                """);

            await Assert.That(claims).Contains(("groups", "private"));
            await Assert.That(claims).Contains(("groups", "friends"));
            await Assert.That(claims).Contains(("sub", "u1"));
        }

        [Test]
        public async Task Scalars_ComeThroughAsThemselves()
        {
            var claims = UserInfoClaims.Parse("""
                {"sub":"u1","email_verified":true,"updated_at":1735689600}
                """);

            await Assert.That(claims).Contains(("email_verified", "True"));
            await Assert.That(claims).Contains(("updated_at", "1735689600"));
        }

        [Test]
        public async Task NestedObjects_AreSkippedRatherThanStringified()
        {
            // A claim whose value is an object is not a role, and serialising it would put JSON
            // where a group name belongs.
            var claims = UserInfoClaims.Parse("""
                {"sub":"u1","address":{"country":"CH"},"groups":["private"]}
                """);

            await Assert.That(claims.Any(c => c.Type == "address")).IsFalse();
            await Assert.That(claims).Contains(("groups", "private"));
        }

        [Test]
        public async Task NullsAndEmptyStrings_AreNotClaims()
        {
            var claims = UserInfoClaims.Parse("""
                {"sub":"u1","name":null,"nickname":""}
                """);

            await Assert.That(claims.Any(c => c.Type == "name")).IsFalse();
            await Assert.That(claims.Any(c => c.Type == "nickname")).IsFalse();
        }

        [Test]
        [Arguments("[]")]
        [Arguments("\"just a string\"")]
        [Arguments("123")]
        public async Task AResponseThatIsNotAnObject_YieldsNothing(string json)
        {
            await Assert.That(UserInfoClaims.Parse(json)).IsEmpty();
        }
    }
}
