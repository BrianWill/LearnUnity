

## the Job System

Rather than creating our own threads, we can use the Job System, which schedules units of work to run in Unity's own threads. The Job System addresses three problems with writing multi-threaded code:

1. Creating and managing our own threads is bothersome and error-prone.
2. Any threads we create wastefully contend with each other and with Unity's own threads for CPU time.
3. Avoiding and detecting race conditions can be very difficult.

Unity creates a thread for every logical core in your system: one core runs the main thread, another runs the graphics thread, and the rest each run a worker thread. The main thread runs our MonoBehavior, coroutine, and ECS system code. Unity's own jobs and the jobs we create run in the worker threads (and in some cases on the main thread).

Once running, a job cannot be interrupted or moved to another thread, and so a job effectively owns its thread until it completes. For this reason (and others), the Job System is inappropriate for I/O tasks.

#### IJob

IJob is the primary interface for defining a job:

```csharp
public struct MyJob : IJob
{
    // each field must be a blittable type or a native container
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

JobHandle h = job.Schedule();       // add job to the queue (will not execute yet)
JobHandle.ScheduleBatchedJobs();    // begin execution of all queued jobs


//...later in the main thread
h.Complete();               // wait for the job to complete
float val = job.b[0];       // 8.0f
val = job.a;                // 5.0f (job.a was unchanged by the job!)
job.b.Dispose();
```

A job should only access its own fields and not touch anything outside the job. Because jobs may run in native threads rather than normal C# threads, bad things can happen if jobs access managed globals (either directly or indirectly).

When a job executes, it is accessing a *copy* of the struct, not the very same value we created on the main thread. Consequently, **changes to the fields within the job are *not* accessible outside the job**. However, when a NativeContainer struct is copied, the copy points to the same native allocated memory, and so **changes in a NativeContainer *are* accessible outside the job**. In the above example, we want to produce one piece of data from the job, so we give it a NativeArray of length one and store the value to return in the array's single slot.

The iterators we get from ComponentGroups (ComponentDataArray, EntityArray, *et al.*) are also valid job fields, but jobs touching entity components should only be created in the context of *JobComponentSystems* (described later).

A job might finish before *Complete()* is called, but calling *Complete()* removes the Job System's internal references to the job and allows the main thread to procede knowing that a job is totally finished. Every job should be completed at some point to avoid a resource leak and to create a sync point past which the job is guaranteed to be done.
Additional *Complete()* calls on a job handle after the first do nothing.

Only the main thread can schedule and complete jobs (for reasons explained [here](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/scheduling_a_job_from_a_job.md)). We usually want the main thread to do business while jobs run on worker threads, so we usually delay calling *Complete()* on a job until we actually need the job completed (which most commonly is at the end of the current frame or at the beginning of the next frame).

To complete multiple jobs but allow them to complete in no particular order, we can use *JobHandle.CompleteAll()*, which takes two or three job handles or a NativeArray\<JobHandle\>:

```csharp
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();
JobHandle c = jobC.Schedule();

// Wait for A, B, and C to complete.
// A, B, and C may complete in parallel and in any order.
JobHandle.CompleteAll(a, b, c);
```

If we want job A to complete before job B starts running, we make A a dependency of B by passing A's handle when scheduling B:

```csharp
// B will not run until after A completes
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule(a);   
```

Calling *Complete()* on a job implicitly first completes any jobs upon which it depends (directly or indirectly). For example, if A is a dependency of B which is a dependency of C, calling *Complete()* on C will first complete A and then B.

Though *Schedule()* only takes one handle, a job can wait for multiple other jobs by using *JobHandle.CombineDependencies()* 
(which takes two or three job handles or a NativeArray\<JobHandle\>):

```csharp
JobHandle d = jobD.Schedule();
JobHandle c = jobC.Schedule();
JobHandle b = jobB.Schedule();
JobHandle combined = JobHandle.CombineDependencies(b, c, d);

// A will not run until after B, C, and D complete.
// B, C, and D may run in parallel.
JobHandle a = jobA.Schedule(combined);  
```

Understand that a queued job is not readied for execution until either:

1. *JobHandle.ScheduleBatchedJobs()* is called, which readies *all* queued jobs
2. *Complete()* is called on its handle
3. *Complete()* is called on the handle of another job for which this job is a (direct or indirect) dependency

When the main thread calls *Complete()*, one or more of the jobs to complete may not have started running yet, and those jobs get priority over any other jobs waiting on the queue. Rather than let a core go to waste, the Job System may use the main thread to run one or more of those jobs. (After all, the main thread would otherwise just sit there and wait, so it might as well chip in!)

#### IJobParallelFor

A ParallelFor job is automatically split behind-the-scenes into multiple actual jobs that can run in parallel, each processing its own range of indexes, *e.g.* one job processes indexes 0 through 99, another processes 100 through 199, another 200 through 299, *etc.*

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
    job.b[i] = i;
}

// add job to the queue (1000 iterations, batch size of 64)
JobHandle h = job.Schedule(1000, 64);
// begin executing queued jobs       
JobHandle.ScheduleBatchedJobs();    


//...later in the main thread
h.Complete();               // wait for the job(s) to complete
float val = job.b[0];         // 5.0f
float val2 = job.b[4];        // 9.0f
job.b.Dispose();
```

Above, the job calls *Execute()* 1000 times, with *i* starting at 0 and incrementing for each other call. The calls are batched into logical jobs that each make 64 calls (except for one batch which makes 40 calls, because 1000 divided by 64 has a remainder of 40). These logical jobs don't have their own handles, but they run as their own units of work and so can run in parallel. (The overhead of 1000 calls is avoided by the compiler inlining the *Execute()* call in each batch loop.)

The larger the batch size, the fewer the queued jobs and so the less overhead, but the fewer opportunities for parallel execution. In general, 32 or 64 is a good batch size when each *Execute()* does very little, and 1 is a good batch size when each *Execute()* does a significant chunk of work.


### safety checks

When one job (or the main thread) writes to a native container, we don't want other jobs that might run in parallel (or the main thread) to read or write the same container, as this likely will create race conditions. Given two conflicting jobs, it is our responsibility to ensure that one will complete before the other starts, either by calling *Complete()* on one before scheduling the other, or by making one a dependency of the other.

The Job System can't decide for us which of two conflicting jobs should run first because that depends upon the logic of what they do! However, the Job System performs safety checks at runtime in the editor to help us detect conflicting jobs. The safety checks track which scheduled jobs touch which native containers, and an exception is thrown when jobs conflict. Some example scenarios:

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
// happenstance of scheduling, so we want an error here.
JobHandle b = jobB.Schedule();     // exception!
```

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
a.Complete();  

// no possible conflict, so no exception
JobHandle b = jobB.Schedule();      // OK
```


```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();

// B will wait for A to complete, so no possible conflict
JobHandle b = jobB.Schedule(a);    // OK
```

If two jobs have the same container marked as \[ReadOnly\], there is no conflict from their concurrent access:

```csharp
public struct MyJob : IJob
{
    [ReadOnly]
    public NativeArray<float> x;

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

While a job is scheduled, accessing any of its native containers in the main thread triggers an exception:

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

job.x[0] = 5.0f;   // OK to touch the container after the job's completion
```

The entity component iterators (ComponentDataArray, EntityArray, *etc.*) have similar safety checks:

```csharp
// assume jobs A and B reference the same ComponentDataArray
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();     // exception!
```

...but because two separate iterators can access the same entity components in memory, the checks are more thorough so as to detect conflicts between separate iterators:

```csharp
// assume jobs A and B reference separate ComponentDataArrays which 
// access the same components in memory
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();     // exception!
```

Additionally, right before a system *OnUpdate()* is called, an exception is thrown if any of the system's ComponentGroups conflict with a scheduled job.

#### JobComponentSystem

A **JobComponentSystem** is like a ComponentSystem but helps us create jobs with appropriate dependencies that avoid entity component access conflicts. Unlike in a normal ComponentSystem, the *OnUpdate()* method receives a job handle and returns a job handle:

1. Immediately after each JobComponentSystem's *OnUpdate()* returns, *JobHandle.ScheduleBatchedJobs()* is called, thus starting execution of any jobs scheduled in the *OnUpdate()*.
2. Immediately before *OnUpdate()*, *Complete()* is called on the JobHandle returned by the system's previous *OnUpdate()* call. So a JobComponentSystem is intended for jobs that at most take a frame to complete.
3. The Job System is aware of which ComponentSystems access which ComponentGroups by virtue of each ComponentSystem's *GetComponentGroup()* calls. The job handle passed to *OnUpdate()* combines the job handles returned by the other JobComponentSystems updated previously in the frame which access ComponentGroups conflicting with those accessed in this JobComponentSystem.

For example, say we have JobComponentSystems A, B, C, D, and E, updated in that order. If E's ComponentGroups conflict with A and D's ComponentGroups, then the JobHandle passed to the *OnUpdate()* of E will combine the JobHandles returned by A and D, such that E's jobs can depend upon A and D's.

The sum effect is that, if all jobs created in every JobComponentSystem:

1. ...only use entity-component iterators from their JobComponentSystem's own ComponentGroups
2. ...*and* use their *OnUpdate()*'s input JobHandle as a dependency (direct or indirect)
3. ...*and* are themselves dependencies of their *OnUpdate()*'s returned JobHandle (or *are* the returned JobHandle itself) 

...then all jobs created within each JobComponentSystem will avoid component access conflicts with the jobs created in all other JobComponentSystems.

For the jobs created *within* a single JobComponentSystem, the editor runtime safety checks will detect when two scheduled jobs conflict in their component access, but it is our responsibility to manually resolve these conflicts by making one job the dependency of the other.

If jobs created in two separate JobComponentSystems conflict, we should specify which *OnUpdate()* should run first with the *UpdateBefore* and *UpdateAfter* attributes. For example, if the jobs of JobComponentSystem A should mutate components before they are read in the jobs of JobComponentSystem B, then we should specify that A must update before B.

Nothing stops us from creating jobs in a JobComponentSystem's *OnUpdate()* which do not depend upon the input JobHandle and which are not themselves dependencies of the returned JobHandle&mdash;but doing so generally defeats the purpose of a JobComponentSystem.

If we want to complete a JobComponentSystem's jobs earlier than the system's next update, we can inject a BarrierSystem: before flushing its EntityCommandBuffers in its update, a BarrierSystem completes the job handles returned by any JobComponentSystems which inject the BarrierSystem.

While it's possible and sometimes useful to create jobs that run longer than a frame, we generally avoid multi-frame jobs which access component groups. Jobs which access entity components should only be created in JobComponentSystems, and these jobs are always completed by the system's next update at the latest. For a multi-frame job which needs to read entity components, we can copy the data to native containers and then use those native containers in the job.