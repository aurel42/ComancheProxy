---
description: Release workflow for ComancheProxy
---

ONLY COMMIT OR RELEASE WHEN PROMPTED BY THE USER. 
Permission for a release can NOT be given implicitly (e.g. by simply including it in a task list or an implementation plan).
This workflow guides the release process for PhileasGo, ensuring code quality, versioning, and documentation.
1.  **Bump Version**:
    - Increment the patch version (e.g., `v0.2.71` -> `v0.2.72`).
2.  **Update History**:
    - Check git diff to ensure you're aware of all changes since the last commit.
    - Open [CHANGELOG.md](file:///c:/Users/aurel/Projects/phileasgo/CHANGELOG.md).
    - Add a new entry at the top for the new version in this format: `## [0.1.6] - 2026-02-21`.
    - **Patch Note Guidelines**:
        - **Focus on macroscopic changes**: Prioritize high-level features and major bug fixes.
        - **Omit Internal Details**: Avoid mentioning CSS properties, alignment adjustments, specific font choices, or internal code refactors. No one cares about your padding.
        - **Omit "Homework"**: Never mention that tests passed, build succeeded, or mention test coverage. Stability and testing are expected defaults, not features.
        - **Be Concise**: Use single, punchy bullet points. Avoid "why" statements, editorializing, or justifying your design choices.
        - **Audience**: Write for a user who hasn't seen the code, not for a collaborator who sat through the dev session.
        - **Fixes: Symptom-Based Description**:
            - Describe the **symptom** the user experienced, not the **solution** you implemented.
            - **Bad**: "Refactored the offset logic to use geodesic distance."
            - **Good**: "Fixed formation balloons appearing in the wrong location."
        - **No Selling**: 
            - Use a dry, factual, and direct tone.
            - Avoid hyperbolic or marketing language: "professional", "premium", "smart", "intelligent".
            - **Bad**: "Implemented a professional cross-fading system for a premium feel."
            - **Good**: "Added volume fades to audio actions to eliminate clicks."
3. Stop here and wait for the user to review CHANGELOG.md.
5.  **Commit**:
    - Commit all changes with a descriptive message.
    ```bash
    git add .
    git commit -m "vX.Y.Z: added feat a, fixed feat b"
    ```
    - Tag the commit with the version number in the format ""vX.Y.Z"
6.  **Done**: The release is now ready to be pushed.
DO NOT PUSH THE RELEASE. THE USER WILL DECIDE WHEN TO PUSH.