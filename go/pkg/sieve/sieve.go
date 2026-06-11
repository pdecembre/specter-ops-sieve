package sieve

type Sieve interface {
	NthPrime(n int64) int64
}

func NewSieve() Sieve {
	panic("unimplemented")
}
