# Publish the first preview

1. Review `README.md`, `upstreams.json`, download origins, licensing notices and the package test results. Test the ZIP on a clean Windows machine with a new data folder before broader distribution.
2. Create an empty GitHub repository, preferably private for the first invited testers. Upload **this clean repository**, not the parent working launchers directory. Do not upload generated artifacts into Git history.
3. Commit the reviewed source on `main`, add the chosen repository as `origin`, and push. No remote or account name is preconfigured.
4. Wait for the `Windows release package` workflow to pass. Its artifact contains the portable ZIP and checksum for review.
5. Tag the intended commit, for example `v0.1.0-alpha.3`, and push that tag. The workflow creates a draft pre-release and attaches the ZIP and checksum; it does not publish it automatically.
6. Review the draft and notes, verify assets, and publish when ready. Invited users need repository read access for a private release.

For later launcher versions, update release notes and the version in `Directory.Build.props`; record upstream revisions actually reviewed/tested. Treat newer upstream builds separately from the launcher version. Avoid advertising this as a playable port or as official upstream distribution.

No GitHub repository has been created or pushed by this preparation step. No credentials are stored in the source. Workflow access to the release token is limited to tag builds; pull-request builds have read-only contents access.
