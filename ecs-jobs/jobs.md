[\<\< prev](intro.md)

## the Job System

Rather than creating our own threads, we can use the Job System, which schedules units of work called 'jobs' to run in Unity's own threads. The Job System addresses three problems with writing multi-threaded code:

1. Creating and managing our own threads is bothersome and error-prone.
2. Any threads we create wastefully contend with each other and with Unity's own threads for CPU time.
3. Maintaining thread safety can be very difficult.

Unity creates a thread for every logical core in your system: one core runs the main thread, another runs the graphics thread, and the rest each run a worker thread. Our MonoBehavior, coroutine, and ECS system code all run in the main thread. Unity's own jobs and the jobs we create run in the worker threads (and in some cases on the main thread).

Once running, a job cannot be interrupted or moved to another thread, and so a job effectively owns its thread until it completes. For this reason (and others), the Job System is inappropriate for I/O tasks: a blocking I/O call in a job would effectively waste the core during the block because no other thread would take over the core while the job is blocked.

### native containers

Unity provides a set of 'native containers': basic data structures implemented as structs pointing into native memory. As this memory is not garbage-collected, it's the programmer's responsibility to deallocate a native container by calling its *Dispose()* method when that native container is no longer needed.

The provided native container types are:

- NativeArray
- NativeSlice (logical indices into a NativeArray)
- NativeList
- NativeHashMap
- NativeQueue
- NativeMultiHashMap

You can implement your own native containers as described [here](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/custom_job_types.md#custom-nativecontainers).

When creating a native container, we specify one of three allocators from which to allocate its memory:

- **Allocator.Temp**: fastest allocation. Temp allocations should not live longer than the frame, and so you should generally dispose of a Temp native container in the same method call in which it's created.
- **Allocator.TempJob**: slower allocation. Safety checks throw an exception if a TempJob allocation lives longer than 4 frames.
- **Allocator.Persistent**: slowest allocation (basically just a wrapper for malloc). Lives indefinitely.

Example:

```csharp
// a Temp array of 5 floats
NativeArray<float> result = new NativeArray<float>(5, Allocator.Temp);
```

#### IJob

IJob is the primary interface for defining a job:

```csharp
public struct MyJob : IJob
{
    public float a;                  // a blittable type
    public NativeArray<float> b;     // a native container
    
    // IJob's single required method
    public void Execute()
    {
        // this change WILL be visible outside the job
        b[0] = b[0] + a;

        // this change will NOT be visible outside the job
        // because the job operates on a copy of the struct
        a = 10.0f;  
    }
}


// ...somewhere in the main thread
// create a job instance and set up its data
MyJob job = new MyJob();
job.a = 5.0f; 

// a native array of one float which must be disposed within 4 frames
job.b = new NativeArray<float>(1, Allocator.TempJob);
job.b[0] = 3.0f;

JobHandle h = job.Schedule();       // add job to the queue (not readied for execution yet)
JobHandle.ScheduleBatchedJobs();    // ready all jobs on queue which aren't already ready


//...later in the main thread
h.Complete();               // wait for the job to complete
float val = job.b[0];       // 8.0f
val = job.a;                // 5.0f (job.a was unchanged by the job!)
job.b.Dispose();
```

Because jobs may run in native threads rather than normal C# threads, bad things can happen if jobs access managed data, including any static fields. In general, a job's *Execute()* method should only access its own locals and the fields of the job struct.

The *Schedule()* method throws an exception if the struct contains any fields which are not native container types or not [blittable types](https://en.wikipedia.org/wiki/Blittable_types) (reference types are not blittable!).

When a job executes, it is accessing a *copy* of the struct, not the very same struct value created on the main thread. Consequently, **changes to the struct fields within the job are *not* accessible outside the job**. However, when a NativeContainer struct is copied, the copy points to the same native allocated memory, and so **changes in a NativeContainer *are* accessible outside the job**. In the above example, we want to produce one piece of data from the job, so we give the job a NativeArray of length one and store the value to return in the array's single slot.

When the main thread calls *Complete()* on a job handle, the call waits for the job to finish if the job hasn't finished already. Calling *Complete()* also removes the Job System's internal references to the job. Every job should be completed at some point to avoid a resource leak and to create a sync point past which the job is guaranteed to be done. After the first *Complete()* call on a job handle, additional *Complete()* calls on that same handle do nothing.

Only the main thread can schedule and complete jobs (for reasons explained [here](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/scheduling_a_job_from_a_job.md)). We usually want the main thread to do business while jobs run on worker threads, so we usually delay calling *Complete()* on a job as long as possible until we absolutely *need* the job completed (which most commonly is at the end of the current frame or after a full frame has elapsed).

To complete multiple jobs but allow them to complete in no particular order, we use *JobHandle.CompleteAll()*, which takes two or three job handles or a NativeArray\<JobHandle\> (for four or more handles):

```csharp
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();
JobHandle c = jobC.Schedule();

// Wait for A, B, and C to complete.
// A, B, and C may run concurrently
JobHandle.CompleteAll(a, b, c);
```

If we want job A to complete before job B starts running, we make A a dependency of B by passing A's handle when scheduling B:

```csharp
// B will not run until after A completes
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule(a);   
```

Calling *Complete()* on a job handle implicitly first completes any jobs upon which it depends (directly or indirectly). For example, if A is a dependency of B which is a dependency of C, calling *Complete()* on C will first complete A and then B.

Though *Schedule()* only takes one handle, a job can wait for multiple other jobs by using *JobHandle.CombineDependencies()*, which takes two or three job handles or a NativeArray\<JobHandle\> (for four or more handles):

```csharp
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();
JobHandle c = jobC.Schedule();
JobHandle combined = JobHandle.CombineDependencies(a, b, c);

// D will not run until after A, B, and C complete.
// A, B, and C may run concurrently.
JobHandle d = jobD.Schedule(combined);  
```

A queued job is not greenlit for execution until either:

1. *JobHandle.ScheduleBatchedJobs()* is called, which readies *all* queued jobs
2. or *Complete()* is called on its handle
3. or *Complete()* is called on the handle of another job for which this job is a (direct or indirect) dependency

When the main thread calls *Complete()*, any jobs it must wait for that aren't yet running will get priority over any other jobs on the queue. Rather than let a core go to waste, the Job System may use the main thread to run one or more of the jobs which *Complete()* is waiting for. (After all, the main thread would otherwise just sit there and wait, so it might as well chip in!)

#### IJobParallelFor

A ParallelFor job is automatically split behind-the-scenes into multiple actual jobs that can run concurrently, each processing its own range of indexes, *e.g.* one job processes indexes 0 through 99, another processes 100 through 199, another 200 through 299, *etc.*

```csharp
public struct MyParallelJob : IJobParallelFor
{
    public float a;
    public NativeArray<float> b;
    
    // 'i' is 0 in first call, then incremented for each subsequent call
    public void Execute(int i)
    {
        b[i] = b[i] + a;
    }
}


// ...somewhere in the main thread

MyParallelJob job = new MyJob();
job.a = 5.0f; 
job.b = new NativeArray<float>(1000, Allocator.TempJob);
for (int i = 0; i < 1000; i++) {
    job.b[i] = i + job.a;
}

// add job to the queue (1000 iterations, batch size of 64)
JobHandle h = job.Schedule(1000, 64);
// begin executing queued jobs       
JobHandle.ScheduleBatchedJobs();    


//...later in the main thread
h.Complete();                 // wait for the job(s) to complete
float val = job.b[0];         // 5.0f
float val2 = job.b[4];        // 9.0f
job.b.Dispose();
```

Above, the job calls *Execute()* 1000 times, with *i* starting at 0 and incrementing for each other call. The calls are batched into logical jobs that each make 64 calls (except for one batch which makes 40 calls, because 1000 divided by 64 has a remainder of 40). These logical jobs don't have their own handles, but they run as their own units of work and so can run concurrently. (The overhead of 1000 calls is avoided by the compiler inlining the *Execute()* call in each batch loop.)

The larger the batch size, the fewer the queued jobs and so the less overhead, but the fewer opportunities for parallel execution. In general, 32 or 64 is a good batch size when each *Execute()* does very little, and 1 is a good batch size when each *Execute()* does a significant chunk of work.

### safety checks

When one job writes to a native container, we don't want other jobs that might run concurrently to read or write the same container, as this likely would cause race conditions. Given two conflicting jobs, it is our responsibility to ensure that one job will finish running before the other starts, either by calling *Complete()* on one before scheduling the other, or by making one a dependency of the other.

The Job System can't decide for us which of two conflicting jobs should run first because that depends upon the logic of what the jobs do! However, the Job System performs safety checks at runtime in the editor to help us detect conflicting jobs. The safety checks track which scheduled jobs touch which native containers, and an exception is thrown when jobs conflict. Some example scenarios:

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();     // exception!
```

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
JobHandle.ScheduleBatchedJobs();  

// Even if job A has finished by now, that is just 
// happenstance of scheduling. If we do not Complete() A before
// scheduling B, we have no guarantee that A finishes before B starts.
JobHandle b = jobB.Schedule();     // exception!
```

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
a.Complete();  

// job A has been completed, so no possible conflict with B
JobHandle b = jobB.Schedule();     // OK
```


```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();

// B will wait for A to finish, so no possible conflict
JobHandle b = jobB.Schedule(a);     // OK
```

If two jobs have the same container marked as \[ReadOnly\], there is no conflict from their concurrent access:

```csharp
public struct MyJob : IJob
{
    [ReadOnly] public NativeArray<float> x;

    public void Execute()
    {...}
}


// ...somewhere in the main thread

MyJob jobA = new MyJob();
jobA.x = new NativeArray<float>(10, Allocator.TempJob);

MyJob jobB = new MyJob();
jobB.x = jobA.x;       // both jobs have the same container

JobHandle a = jobA.Schedule();

// OK because both jobs only read the container
JobHandle b = jobB.Schedule();   
```

While a job is scheduled, accessing the contents of any of its native containers in the main thread triggers an exception:

```csharp
public struct MyJob : IJob
{
    public NativeArray<float> x;

    public void Execute()
    {...}
}


// ...somewhere in the main thread

MyJob job = new MyJob();
job.x = new NativeArray<float>(10, Allocator.TempJob);
JobHandle h = job.Schedule();

job.x[0] = 5.0f;   // exception! conflict with the container's use in a scheduled job   

h.Complete();       

job.x[0] = 5.0f;   // OK to access the container contents after the job's completion
```

[next \>\>](ecs.md)
