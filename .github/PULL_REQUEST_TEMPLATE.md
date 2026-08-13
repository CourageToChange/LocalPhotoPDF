## What does this change?

<!-- A sentence or two. If it fixes an open issue, write "Fixes #123" and GitHub will link them. -->

## Why?

<!-- What problem does it solve? If there is an issue discussing it, link that instead of repeating it. -->

## How did you test it?

<!-- What you actually ran or clicked. "I converted 20 HEIC photos and checked the output" is a
     perfectly good answer. -->

## Checklist

- [ ] `dotnet build .\LocalPhotoPDF.sln -c Release` succeeds with **no new warnings**
      (warnings are errors in this project, so this is not optional)
- [ ] `dotnet test .\LocalPhotoPDF.sln -c Release` passes
- [ ] If behaviour changed, I added or updated a test
- [ ] If a NuGet dependency changed, the updated `packages.lock.json` is in this PR
- [ ] If it changes what users see, `USER-GUIDE.md` is updated
- [ ] I have added an entry to `CHANGELOG.md` under "Unreleased"

## Anything you are unsure about?

<!-- Not sure about something? Say so here. An unfinished PR with a question attached is fine. -->
