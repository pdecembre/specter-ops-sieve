namespace Sieve.Implementation.Validation;

/// <summary>
/// Utility methods for validating prime numbers and prime sequences.
/// Thread-safe: Yes (stateless static methods).
///
/// Design notes:
/// 1) <see cref="IsPrime(long)"/> is optimized for correctness and readability over raw speed.
/// 2) <see cref="AreConsecutivePrimes(ReadOnlySpan{long})"/> is intended primarily for
///    validation/testing workflows where explicit correctness checks are preferable.
/// 3) Both methods are deterministic and side-effect free.
/// </summary>
public static class PrimeValidator
{
    /// <summary>
    /// Checks whether a number is prime using trial division up to sqrt(n).
    ///
    /// Mathematical basis:
    /// If n is composite, then n = a * b for integers a,b &gt; 1.
    /// At least one of a or b must satisfy a &lt;= sqrt(n), so it is sufficient
    /// to test divisors only up to sqrt(n).
    ///
    /// Complexity:
    /// - Time: O(sqrt(n))
    /// - Space: O(1)
    /// </summary>
    /// <param name="n">Candidate number.</param>
    /// <returns>True if n is prime; otherwise false.</returns>
    public static bool IsPrime(long n)
    {
        // Guard 1: domain exclusion for non-primes below 2.
        // Prime numbers are defined on positive integers greater than 1.
        // Returning here keeps invalid/degenerate inputs out of the divisor logic.
        if (n < 2)
        {
            return false;
        }

        // Guard 2: special-case the only even prime.
        // This allows the parity branch below to reject every other even number.
        if (n == 2)
        {
            return true;
        }

        // Guard 3: eliminate all even composites in O(1).
        // '(n & 1) == 0' checks parity without modulo and is explicit about intent:
        // numbers divisible by 2 (other than 2 itself) are composite.
        if ((n & 1) == 0)
        {
            return false;
        }

        // Compute the inclusive divisor boundary.
        // We only need to search [3, sqrt(n)] because any factor pair beyond sqrt(n)
        // would imply a corresponding factor below sqrt(n) that we would already detect.
        var limit = (long)Math.Sqrt(n);

        // Main search loop: test odd divisors only.
        // Step size 2 skips evens, reducing work by roughly half after parity filtering.
        // Loop invariant: all previously tested odd divisors in [3, i-2] do not divide n.
        for (long i = 3; i <= limit; i += 2)
        {
            // Composite witness check:
            // if i divides n exactly, n has a non-trivial factor and cannot be prime.
            if (n % i == 0)
            {
                return false;
            }
        }

        // Exhaustive search over the required divisor range found no factors.
        // Therefore n is prime.
        return true;
    }

    /// <summary>
    /// Validates that all candidates are prime and no prime is missing between adjacent values.
    ///
    /// Validation contract:
    /// 1) Every element must be prime.
    /// 2) Sequence must be strictly increasing.
    /// 3) For each adjacent pair (p[i-1], p[i]), the open interval (p[i-1], p[i])
    ///    must contain no prime values.
    ///
    /// This method answers: "Does this span represent a contiguous slice of the prime sequence?"
    ///
    /// Complexity:
    /// - Worst-case time can be high for large gaps because each interior value is primality-checked.
    /// - Space: O(1) extra memory.
    /// </summary>
    /// <param name="candidates">Sequence to validate.</param>
    /// <returns>True if candidates represent consecutive prime numbers; otherwise false.</returns>
    public static bool AreConsecutivePrimes(ReadOnlySpan<long> candidates)
    {
        // Vacuous truth for empty input:
        // there is no candidate that violates primality or adjacency constraints.
        // This behavior is useful in pipelines where empty batches are valid artifacts.
        if (candidates.Length == 0)
        {
            return true;
        }

        // Single forward pass over candidates:
        // - perform local primality validation
        // - perform pairwise adjacency validation for i > 0
        for (var i = 0; i < candidates.Length; i++)
        {
            // Rule 1: each candidate must itself be prime.
            // Early return gives immediate failure context and avoids unnecessary extra checks.
            if (!IsPrime(candidates[i]))
            {
                return false;
            }

            // Index 0 has no left neighbor; adjacency rules start at i = 1.
            if (i == 0)
            {
                continue;
            }

            // Rule 2: strict monotonicity.
            // Equal or descending values violate the natural ordering of primes.
            if (candidates[i] <= candidates[i - 1])
            {
                return false;
            }

            // Rule 3: no skipped primes between neighbors.
            // We inspect every integer in the open interval (prev, current).
            // Finding any prime in this interval is a proof that input is not consecutive.
            // Example failure: [11, 17] fails because 13 is prime and missing.
            for (long value = candidates[i - 1] + 1; value < candidates[i]; value++)
            {
                if (IsPrime(value))
                {
                    return false;
                }
            }
        }

        // All three rules held for the entire span.
        return true;
    }
}