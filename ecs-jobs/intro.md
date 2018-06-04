Unity version 2018.1 introduces a few major new features for achieving high performance:

- **[ECS (Entity Component System)](ecs.md)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear fashion: tightly packed, contiguous, and accessible in sequence. By separating code from data, ECS not only improves performance, it (arguably) improves code structure over the traditional Object-Oriented approach.
- The **[Job System](jobs.md)** farms units of work called 'jobs' out to threads while helping us avoid race conditions.
- The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD), which are particularly beneficial for math-heavy code. The Burst compiler is not a general-purpose C# compiler: it only works on job code, which is written in a subset of C# called HPC# (High Performance C#).

ECS and the Job System can be used separately, but they are highly complementary: ECS guarantees data is layed out linearly in memory, which speeds up job code accessing the data and gives the Burst compiler more optimization opportunities.