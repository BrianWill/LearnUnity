

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
    // the fields can only be blitable types and native containers
    public float a;
    public NativeArray<float> b;

    // IJob's single required method
    public void Execute()
    {
        // this change WILL be visible outside the job
        b[0] = b[0] + a;

        // this change will NOT be visible outside the job 
        // (explained in text below)
        a = 10.0f;  
    }
}


// ...somewhere in the main thread

// create an instance, set up its data, 
// and schedule the job
MyJob job = new MyJob();
job.a = 5.0f; 
// a native array of one float which must 
// be disposed within 4 frames
job.b = new NativeArray<float>(1, Allocator.TempJob);
job.b[0] = 3.0f;

JobHandle h = job.Schedule();       // add job to the queue (will not execute yet)
JobHandle.ScheduleBatchedJobs();    // begin execution of queued jobs


//...later in the main thread
h.Complete();               // wait for the job to complete
float val = job.b[0];       // 8.0f
val = job.a;                // 5.0f (job.a was unchanged by the job!)
job.b.Dispose();
```

A job should only access its own fields and not touch anything outside the job. Because a job runs outside of C#'s usual context, very bad things can happen if a job accesses globals (either directly or indirectly).

When a job executes, it is accessing a *copy* of the struct, not the very same value we created on the main thread. Consequently, **changes to the fields within the job are *not* accessible outside the job**. However, when a NativeContainer struct is copied, the copy points to the same native allocated memory, and so **changes in a NativeContainer *are* accessible outside the job**. In the above example, we want to produce one piece of data from the job, so we give it a NativeArray of length one.

The iterators we get from ComponentGroups (ComponentDataArray, EntityArray, *et al.*) are also valid job fields, but jobs touching entity components should only be created in the context of *JobComponentSystems* (described later).

A job may finish before *Complete()*, but calling *Complete()* allows the main thread to procede knowing that a job is totally finished and its internal references removed from the Job System. Every job should be completed at some point because failing to do so effectively creates a resource leak and because code logic generally demands a sync point past which a job is guaranteed to be finished.

Only the main thread can schedule and complete jobs (for reasons explained [here](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/scheduling_a_job_from_a_job.md)). We usually want the main thread to do more business while jobs run on worker threads, so we usually delay calling *Complete()* on a job until we actually need the job completed (which most commonly is at the end of the current frame or at the beginning of the next frame).

To complete multiple jobs but still allow them to execute in parallel, we can use *JobHandle.CompleteAll()*, which takes two or three job handles or a NativeArray\<JobHandle\>:

```csharp
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();
JobHandle c = jobC.Schedule();
// wait for A, B, and C to complete
// A, B, and C may run in parallel
JobHandle.CompleteAll(a, b, c);
```

If we want job A to complete before job B starts running, we make A a dependency of B by passing job A's handle when scheduling B:

```csharp
// B will not run until after A completes
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule(a);   
```

Calling *Complete()* on a job implicitly first completes its any other jobs upon which it depends (directly or indirectly). For example, if A is a dependency of B which is a dependency of C, calling *Complete()* on C will first complete A and then B.

Though *Schedule()* only takes one handle, a job can wait for multiple other jobs by using *JobHandle.CombineDependencies()* 
(which takes two or three job handles or a NativeArray\<JobHandle\>):

```csharp
// A will not run until after B, C, and D complete
// B, C, and D may run in parallel
JobHandle d = jobD.Schedule();
JobHandle c = jobC.Schedule();
JobHandle b = jobB.Schedule();
JobHandle combined = JobHandle.CombineDependencies(b, c, d);
JobHandle a = jobA.Schedule(combined);  
```

Understand that a queued job is not readied for execution until:

1. *JobHandle.ScheduleBatchedJobs()* is called, which readies *all* queued jobs
2. *Complete()* is called on its handle
3. *Complete()* is called on the handle of another job for which this job is a (direct or indirect) dependency

When the main thread calls *Complete()*, one or more of the jobs to complete may not have started running yet, and those jobs get priority over any other jobs waiting on the queue. Rather than let a core go to waste, the Job System may use the main thread to run one or more of those jobs. (After all, the main thread would otherwise just sit there and wait, so it might as well chip in!)

#### IJobParallelFor

A ParallelFor job is automatically split behind-the-scenes into multiple actual jobs that can run in parallel, each processing its own range of indexes, *e.g.* one job handles indexes 0-99, another handles 100-199, another 200-299, *etc.*

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

The larger the batch size, the fewer the logical jobs and so the less overhead, but the fewer opportunities for parallel execution. In general, 32 or 64 is a good batch size when each *Execute()* does very little, and 1 is a good batch size when each *Execute()* does a significant chunk of work.


### safety checks

When one job (or the main thread) writes to a native container, we don't want other jobs (or the main thread) that might run in parallel to read or write the same container, as this likely will create race condition bugs. Given two conflicting jobs, it is our responsibility to ensure that one will complete before the other starts by making one a dependency of the other.

The Job System can't decide for us which of two conflicting jobs should run first because that depends upon the logic of what they do! However, the Job System performs safety checks at editor runtime to help us detect conflicting jobs. The safety checks track which scheduled jobs touch which native containers, and an exception is thrown when jobs conflict. Some example scenarios:

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
handleA.Complete();  

// no possible conflict, so no exception
JobHandle b = jobB.Schedule();      // OK
```


```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle handleA = jobA.Schedule();

// B will wait for A to complete, so no possible conflict
JobHandle handleB = jobB.Schedule(handleA);    // OK
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

While a job is scheduled but not complete, accessing any of its native containers in the main thread also triggers an exception:

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

// exception! conflict with the container's use in scheduled job       
job.x[0] = 5.0f;

h.Complete();       

// OK to touch the container after the job's completion
job.x[0] = 5.0f;     
```

#### JobComponentSystem

A **JobComponentSystem** is like a ComponentSystem, but the OnUpdate method receives a job handle and returns a job handle. 


who is completing the returned job?

When ECS and the Job System are fully utilized together, most systems will do most of their work in jobs. The *JobComponentSystem* class not only expresses this pattern more conveniently, it allows us to define job(s)



### integrating ECS with GameObjects/Components

As mentioned, ECS as of yet has few stock components and systems, and so lacks most of the functionality of the old GameObjects and their Components. Thus we may wish to still use GameObjects along with ECS.