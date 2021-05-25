==> Set up GitHub OAuth access Azure API Management (didn't use, didn't require the extra bells and whistles)
https://github.com/MicrosoftDocs/azure-docs/blob/master/articles/api-management/api-management-howto-oauth2.md

==> Use GitHUb's OctoKit library to authorize users against their GitHub account.
https://github.com/octokit/octokit.net/blob/main/docs/oauth-flow.md

dotnet user-secrets set GitHub:ClientSecret "secret"
Import into Azure Key Store GitHub--ClientSecret "secret"

==> Web API URLs
https://localhost:5001/swagger/index.html
https://localhost:5001/api/sipaccounts
https://localhost:5001/api/sipdomains
https://localhost:5001/api/sipregistrarbindings
https://localhost:5001/api/sipcalls
https://localhost:5001/api/cdrs

==> Test Web API:
curl -X POST "https://localhost:5001/api/SIPAccounts" -H "accept: text/plain" -H "Content-Type: application/json" -d "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"sipUsername\":\"aaron\",\"sipPassword\":\"password\",\"owner\":\"\",\"Sipdomain\":\"aspnet.sipsorcery.com\",\"isDisabled\":false,\"inserted\":\"2020-12-29T00:00:00.0000000+00:00\"}"