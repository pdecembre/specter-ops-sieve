# Detailed C# Implementation Plan for Nth Prime API

## 1. Problem Restatement in Precise Terms

We need an implementation of the method below in [csharp/Sieve/Sieve.cs](csharp/Sieve/Sieve.cs):

- NthPrime(n): return the prime at zero-based index n
- zero-based index means:
  - NthPrime(0) = 2
  - NthPrime(1) = 3
  - NthPrime(2) = 5

Required verification values come from [README.md](README.md):

- NthPrime(0) = 2
- NthPrime(19) = 71
- NthPrime(99) = 541
- NthPrime(500) = 3_581
- NthPrime(986) = 7_793
- NthPrime(2_000) = 17_393
- NthPrime(1_000_000) = 15_485_867
- NthPrime(10_000_000) = 179_424_691

This is not a small-input exercise. The implementation must be both mathematically correct and computationally efficient for very large n.

## 2. Atomic Definition of Prime and Composite

### 2.1 Divisibility
For integers a and b (with b != 0), b divides a if there exists an integer k such that:

a = b * k

Example:

- 3 divides 21 because 21 = 3 * 7
- 4 does not divide 21 because there is no integer k with 21 = 4 * k

### 2.2 Prime number
An integer p is prime if:

- p > 1
- the only positive divisors of p are 1 and p

Examples:

- 2 is prime (divisors: 1, 2)
- 3 is prime (divisors: 1, 3)
- 5 is prime (divisors: 1, 5)

### 2.3 Composite number
An integer c is composite if:

- c > 1
- c has at least one divisor d with 1 < d < c

Examples:

- 4 is composite because 4 = 2 * 2
- 21 is composite because 21 = 3 * 7

### 2.4 Why some numbers do not work as primes
A number fails to be prime as soon as we can express it as a product of smaller integers greater than 1.

- 9 fails because 9 = 3 * 3
- 15 fails because 15 = 3 * 5
- 35 fails because 35 = 5 * 7

This is the exact condition the sieve exploits: if a number appears in a multiplication table of a smaller prime, it is not prime.

## 3. Core Theorems Used by the Algorithm

## 3.1 Factor-pair theorem and sqrt bound
If n is composite, then n has a factor f with f <= sqrt(n).

Proof idea:

- If n = a * b and both a and b were greater than sqrt(n), then a * b > n, contradiction.
- Therefore at least one of a or b must be <= sqrt(n).

Consequence:
To prove n is prime, it is enough to check divisibility by candidates up to sqrt(n), not all the way to n - 1.

## 3.2 Fundamental theorem of arithmetic
Every integer n > 1 has a unique prime factorization up to ordering.

Example:

- 84 = 2 * 2 * 3 * 7

Consequence for sieve:
Every composite has at least one prime factor, so if we eliminate multiples of all primes up to sqrt(limit), every remaining unmarked number is prime.

## 3.3 Why marking from p^2 is correct
The notation p^2 means p * p (p multiplied by itself), also called "p squared".

Examples:

- 3^2 = 3 * 3 = 9
- 5^2 = 5 * 5 = 25
- 7^2 = 7 * 7 = 49

When sieving with a prime p:

- Multiples below p^2 are p * 2, p * 3, ..., p * (p-1)
- Each of those has a smaller factor than p, so they were already marked when processing smaller primes

Example:

- When processing prime 5, multiples below 5^2 = 25 are: 5*2=10, 5*3=15, 5*4=20
- 10 was marked by 2 (it is 2*5)
- 15 was marked by 3 (it is 3*5)
- 20 was marked by 2 (it is 2*10)
- So we skip these and begin marking at 5^2 = 25, then 25+10=35, then 35+10=45, etc.

Therefore we can start at p^2 and skip redundant work.

## 3.4 Prime Number Theorem (used for upper bounds)
Let p_k be the k-th prime in one-based indexing.
As k grows:

p_k is approximately k * ln(k)

A practical upper bound for k >= 6 is:

p_k < k * (ln(k) + ln(ln(k)))

Why this matters:
We need a limit before running sieve. This theorem gives a strong estimate so we allocate enough space once (or with a rare retry).

## 4. Why Naive Methods Are Too Slow Here

Naive method:

- Candidate x starts at 2
- Test primality of each x by trial division
- Count primes until index n reached

For n = 10_000_000 this is too costly:

- Huge number of candidates to test
- Repeated division operations
- Poor cache behavior and repeated work

Sieve method does bulk elimination and avoids repeated primality recomputation.

## 5. Sieve of Eratosthenes From Low Level Up

### Historical Origin
The Sieve of Eratosthenes is one of the oldest known algorithms for finding prime numbers, dating back to ancient Greece around 240 BCE.

Eratosthenes of Cyrene (c. 276–194 BCE) was a Greek mathematician, geographer, poet, and astronomer who served as the chief librarian at the Library of Alexandria. He is best known for:

- Calculating the Earth's circumference with remarkable accuracy
- Creating one of the first known maps of the world
- Inventing this elegant prime-finding algorithm

The algorithm is called a "sieve" because it systematically filters out composite numbers, leaving only primes behind—much like a physical sieve separates fine material from coarse. Ancient scholars would use a board with holes representing numbers, and physically remove pegs or counters at composite positions.

The method's enduring value lies in its simplicity and efficiency. Over 2,200 years later, the core idea remains one of the fastest ways to generate many primes, and modern optimizations (like odd-only storage described below) build directly on Eratosthenes' insight that multiples of primes cannot themselves be prime.


## 5.1 Data representation choice
Direct array of all integers up to limit wastes space on even numbers.

Optimization:

